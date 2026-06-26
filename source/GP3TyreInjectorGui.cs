using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;

[assembly: AssemblyTitle("GP3 Tyre Fix Injector")]
[assembly: AssemblyDescription("Runtime in-memory fix for GP3 non-power-of-two tyre textures plus force-3D-wheels-in-all-views. Supports GP3 v1.13 and GP3 2000. The on-disk game exe is never modified.")]
[assembly: AssemblyCompany("James Knopp")]
[assembly: AssemblyProduct("GP3 Tyre Fix Injector")]
[assembly: AssemblyCopyright("Copyright © James Knopp 2026")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// GP3 Tyre Fix Injector - Windows GUI (multi-build, runtime/in-memory).
//  Pick version -> locate the DECRYPTED exe -> validate (PE + all 5 signatures + build match)
//  -> Launch & inject (starts GPxPatch, waits for the GP3 process, injects in memory).
//  On-disk exe is never modified. Settings saved next to the app. Always runs as admin.
//  Copyright (C) James Knopp 2026
class GP3TyreInjectorGui
{
    // ---------- win32 ----------
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint a, bool inh, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FlushInstructionCache(IntPtr h, IntPtr addr, IntPtr size);
    const uint ACCESS = 0x0008 | 0x0010 | 0x0020;
    const uint IMAGE_BASE = 0x400000;
    const uint DISP_V113 = 0x7E545C, DISP_2000 = 0xB61260;

    // ---------- patch sites (address-independent signatures; -1 = wildcard) ----------
    class Site { public string Name; public int[] Pre; public int[] OrigJump; public byte[] PatchJump; public int[] Post; }
    static readonly Site[] Sites = new Site[]
    {
        new Site{ Name="POW2 A", Pre=new[]{0xF6,0xC1,0x02}, OrigJump=new[]{0x74,-1}, PatchJump=new byte[]{0x90,0x90},
                  Post=new[]{0x8B,0x7C,0x24,0x24,0xBA,0x01,0x00,0x00,0x00,0x3B,0xFA,0xC6,0x05,-1,-1,-1,-1,0x01} },
        new Site{ Name="POW2 B", Pre=new[]{0xF6,0xC1,0x02}, OrigJump=new[]{0x74,-1}, PatchJump=new byte[]{0x90,0x90},
                  Post=new[]{0xBE,0x01,0x00,0x00,0x00,0xC6,0x05,-1,-1,-1,-1,0x01} },
        new Site{ Name="POW2 C", Pre=new[]{0xF6,0xC2,0x02}, OrigJump=new[]{0x74,-1}, PatchJump=new byte[]{0x90,0x90},
                  Post=new[]{0xB9,0x01,0x00,0x00,0x00,0xC6,0x05,-1,-1,-1,-1,0x01} },
        new Site{ Name="POW2 D", Pre=new[]{0xF6,0xC2,0x02}, OrigJump=new[]{0x74,-1}, PatchJump=new byte[]{0x90,0x90},
                  Post=new[]{0x8B,0xCE,0x3B,0xC6,0x89,0x4C,0x24,-1} },
        new Site{ Name="3D wheels", Pre=new[]{0x80,0xBA,-1,-1,-1,-1,0x01}, OrigJump=new[]{0x0F,0x8C,-1,-1,-1,-1}, PatchJump=new byte[]{0x90,0x90,0x90,0x90,0x90,0x90},
                  Post=new[]{0xE8} },
    };
    static int[] Concat(params int[][] ps){ var l=new List<int>(); foreach(var p in ps) l.AddRange(p); return l.ToArray(); }
    static int[] AsInts(byte[] b){ var r=new int[b.Length]; for(int i=0;i<b.Length;i++) r[i]=b[i]; return r; }
    static List<int> FindAll(byte[] hay,int[] pat){ var h=new List<int>(); int m=pat.Length;
        for(int i=0;i<=hay.Length-m;i++){ bool ok=true; for(int j=0;j<m;j++){ if(pat[j]>=0 && hay[i+j]!=(byte)pat[j]){ok=false;break;} } if(ok) h.Add(i); } return h; }
    // 0=unpatched(matchStart set) 1=already patched -1=not found
    static int Classify(byte[] d, Site s, out int matchStart){ matchStart=-1;
        var hu=FindAll(d,Concat(s.Pre,s.OrigJump,s.Post)); if(hu.Count==1){ matchStart=hu[0]; return 0; }
        if(hu.Count==0){ var hp=FindAll(d,Concat(s.Pre,AsInts(s.PatchJump),s.Post)); if(hp.Count>=1){ matchStart=hp[0]; return 1; } } return -1; }
    static uint WheelDisp(byte[] code){ int st; if(Classify(code,Sites[4],out st)>=0 && st>=0) return BitConverter.ToUInt32(code,st+2); return 0; }
    static string BuildName(uint disp){ if(disp==DISP_V113) return "GP3 v1.13"; if(disp==DISP_2000) return "GP3 2000"; return disp==0?"GP3":("GP3 (flag 0x"+disp.ToString("X")+")"); }

    // ---------- validation against an on-disk FILE (the "decrypted" check) ----------
    class Vr { public bool isPE; public int sites; public uint disp; }
    static Vr ValidateFile(string path){
        var v=new Vr();
        byte[] f; try { f=File.ReadAllBytes(path); } catch { return v; }
        if(f.Length<0x200 || f[0]!=0x4D || f[1]!=0x5A) return v;
        int e=BitConverter.ToInt32(f,0x3C); if(e<=0 || e+0x108>f.Length || f[e]!=0x50 || f[e+1]!=0x45) return v;
        v.isPE=true;
        ushort nSec=BitConverter.ToUInt16(f,e+6); ushort sOpt=BitConverter.ToUInt16(f,e+0x14);
        int so=e+0x18+sOpt; uint tVA=0,tSz=0,tRaw=0;
        for(int i=0;i<nSec;i++){ int o=so+i*0x28; if(o+0x28>f.Length) break; string nm=Encoding.ASCII.GetString(f,o,8).TrimEnd('\0');
            if(nm==".text"){ tSz=BitConverter.ToUInt32(f,o+8); tVA=BitConverter.ToUInt32(f,o+0xC); tRaw=BitConverter.ToUInt32(f,o+0x14); break; } }
        if(tRaw==0||tRaw>=f.Length) return v;
        int n=(int)Math.Min(tSz,(uint)(f.Length-tRaw)); byte[] code=new byte[n]; Array.Copy(f,(int)tRaw,code,0,n);
        int found=0; foreach(var s in Sites){ int st; if(Classify(code,s,out st)>=0) found++; }
        v.sites=found; v.disp=WheelDisp(code); return v;
    }

    // =====================================================================
    [STAThread] static void Main(){ Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); }

    class MainForm : Form
    {
        RadioButton rbV113, rb2000; TextBox txtExe, txtGpx, txtLog; Button bExe, bGpx, bGo; Label lblStat;
        readonly string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GP3TyreInjector.cfg");
        volatile bool running; Thread worker; readonly HashSet<int> done = new HashSet<int>();

        public MainForm(){
            Text="GP3 tyre fix injector"; FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(486,452); Font=new Font("Segoe UI",9f);
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}

            Add(new Label{ Text="Game version", AutoSize=true, Location=new Point(14,12), ForeColor=SystemColors.GrayText });
            rbV113=new RadioButton{ Text="GP3 (v1.13)", Location=new Point(14,32), AutoSize=true };
            rb2000=new RadioButton{ Text="GP3 2000", Location=new Point(140,32), AutoSize=true, Checked=true };
            rbV113.CheckedChanged+=(s,e)=>Recheck(); rb2000.CheckedChanged+=(s,e)=>Recheck(); Add(rbV113); Add(rb2000);

            Add(new Label{ Text="Decrypted game .exe", AutoSize=true, Location=new Point(14,64), ForeColor=SystemColors.GrayText });
            txtExe=new TextBox{ Location=new Point(14,84), Size=new Size(360,24) }; txtExe.TextChanged+=(s,e)=>Recheck(); Add(txtExe);
            bExe=new Button{ Text="Browse…", Location=new Point(382,83), Size=new Size(86,26) }; bExe.Click+=(s,e)=>Pick(txtExe); Add(bExe);
            lblStat=new Label{ AutoSize=false, Location=new Point(14,114), Size=new Size(454,20), Text="" }; Add(lblStat);

            Add(new Label{ Text="GPxPatch (launcher)", AutoSize=true, Location=new Point(14,142), ForeColor=SystemColors.GrayText });
            txtGpx=new TextBox{ Location=new Point(14,162), Size=new Size(360,24) }; Add(txtGpx);
            bGpx=new Button{ Text="Browse…", Location=new Point(382,161), Size=new Size(86,26) }; bGpx.Click+=(s,e)=>Pick(txtGpx); Add(bGpx);

            bGo=new Button{ Text="Launch & inject", Location=new Point(14,198), Size=new Size(454,34), Font=new Font("Segoe UI",10f,FontStyle.Bold) };
            bGo.Click+=(s,e)=>Toggle(); Add(bGo);

            txtLog=new TextBox{ Location=new Point(14,244), Size=new Size(454,166), Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, BackColor=Color.FromArgb(30,30,30), ForeColor=Color.Gainsboro, Font=new Font("Consolas",9f) };
            Add(txtLog);

            Add(new Label{ AutoSize=true, Location=new Point(14,418), ForeColor=SystemColors.GrayText, Text="v"+AsmVer()+"   ·   "+AttrCopyright() });
            var lnkAbout=new LinkLabel{ AutoSize=true, Text="About", Location=new Point(433,418) };
            lnkAbout.Click+=(s,e)=>ShowAbout(); Add(lnkAbout);

            LoadCfg(); Recheck();
            FormClosing+=(s,e)=>{ running=false; };
        }
        void Add(Control c){ Controls.Add(c); }

        void Pick(TextBox t){ using(var d=new OpenFileDialog{ Filter="Executable (*.exe)|*.exe|All files|*.*" }){ try{ if(File.Exists(t.Text)) d.FileName=t.Text; }catch{} if(d.ShowDialog(this)==DialogResult.OK){ t.Text=d.FileName; } } }

        bool selV113 { get { return rbV113.Checked; } }
        bool validOk;
        void Recheck(){
            validOk=false;
            string p=txtExe.Text.Trim().Trim('"');
            if(p.Length==0){ SetStat("Choose your decrypted GP3 .exe.", SystemColors.GrayText); }
            else if(!File.Exists(p)){ SetStat("File not found.", Color.Firebrick); }
            else {
                var v=ValidateFile(p);
                if(!v.isPE) SetStat("Not a valid Windows executable.", Color.Firebrick);
                else if(v.sites<5) SetStat("Encrypted or unsupported exe ("+v.sites+"/5 sites found) — use the DECRYPTED GP3 exe.", Color.Firebrick);
                else {
                    uint exp = selV113 ? DISP_V113 : DISP_2000;
                    if(v.disp==exp){ SetStat("✓  Decrypted "+BuildName(v.disp)+" verified — all 5 patch sites found.", Color.SeaGreen); validOk=true; }
                    else SetStat("This is "+BuildName(v.disp)+", but you selected "+(selV113?"GP3 (v1.13)":"GP3 2000")+".", Color.DarkOrange);
                }
            }
            if(!running) bGo.Enabled = validOk && File.Exists(txtGpx.Text.Trim().Trim('"'));
        }
        void SetStat(string t, Color c){ lblStat.ForeColor=c; lblStat.Text=t; }

        void Toggle(){
            if(running){ running=false; bGo.Text="Launch & inject"; SetControls(true); Log("stopped watching."); return; }
            string gpx=txtGpx.Text.Trim().Trim('"');
            if(!validOk){ Recheck(); if(!validOk) return; }
            if(!File.Exists(gpx)){ MessageBox.Show(this,"GPxPatch.exe not found.","GP3 tyre fix injector"); return; }
            SaveCfg();
            try{ Process.Start(new ProcessStartInfo{ FileName=gpx, WorkingDirectory=Path.GetDirectoryName(gpx), UseShellExecute=true }); }
            catch(Exception ex){ MessageBox.Show(this,"Could not launch GPxPatch:\n"+ex.Message,"GP3 tyre fix injector"); return; }
            Log("launched "+Path.GetFileName(gpx)+" — waiting for the GP3 process…");
            done.Clear(); running=true; bGo.Text="Stop watching"; SetControls(false);
            worker=new Thread(Watch){ IsBackground=true }; worker.Start();
        }
        void SetControls(bool on){ rbV113.Enabled=rb2000.Enabled=txtExe.Enabled=txtGpx.Enabled=bExe.Enabled=bGpx.Enabled=on; }

        void Watch(){
            while(running){
                try{ foreach(var pr in Process.GetProcesses()){
                    string nm; try{ nm=pr.ProcessName; }catch{ continue; }
                    if(!nm.StartsWith("GP3",StringComparison.OrdinalIgnoreCase)) continue;
                    if(done.Contains(pr.Id)) continue;
                    Inject(pr);
                } }catch{}
                Thread.Sleep(25);
            }
        }

        void Inject(Process pr){
            IntPtr h=OpenProcess(ACCESS,false,pr.Id);
            if(h==IntPtr.Zero){ Log("found "+pr.ProcessName+".exe (pid "+pr.Id+") but cannot open it (access denied)."); done.Add(pr.Id); return; }
            try{
                byte[] hdr=new byte[0x1000]; IntPtr n;
                if(!(ReadProcessMemory(h,(IntPtr)IMAGE_BASE,hdr,(IntPtr)hdr.Length,out n) && (int)n==hdr.Length)) return;
                if(hdr[0]!=0x4D||hdr[1]!=0x5A){ done.Add(pr.Id); return; }
                int e=BitConverter.ToInt32(hdr,0x3C); if(e<=0||e>0xF00) return;
                ushort nSec=BitConverter.ToUInt16(hdr,e+6); ushort sOpt=BitConverter.ToUInt16(hdr,e+0x14);
                int so=e+0x18+sOpt; uint tVA=0,tSz=0;
                for(int i=0;i<nSec;i++){ int o=so+i*0x28; string nmS=Encoding.ASCII.GetString(hdr,o,8).TrimEnd('\0');
                    if(nmS==".text"){ tSz=BitConverter.ToUInt32(hdr,o+8); tVA=BitConverter.ToUInt32(hdr,o+0xC); break; } }
                if(tVA==0||tSz==0||tSz>0x400000){ done.Add(pr.Id); return; }
                byte[] code=new byte[tSz];
                if(!(ReadProcessMemory(h,(IntPtr)(IMAGE_BASE+tVA),code,(IntPtr)code.Length,out n) && (int)n==code.Length)) return;
                int applied=0, already=0, missing=0;
                foreach(var s in Sites){ int st; int c=Classify(code,s,out st);
                    if(c==0){ uint va=IMAGE_BASE+tVA+(uint)(st+s.Pre.Length); IntPtr w;
                        if(WriteProcessMemory(h,(IntPtr)va,s.PatchJump,(IntPtr)s.PatchJump.Length,out w)&&(int)w==s.PatchJump.Length){ FlushInstructionCache(h,(IntPtr)va,(IntPtr)s.PatchJump.Length); applied++; } else missing++; }
                    else if(c==1) already++; else missing++; }
                if(applied==0&&already==0){ Log("GP3 pid "+pr.Id+": no patch sites matched — unsupported build, skipped."); done.Add(pr.Id); return; }
                Log(BuildName(WheelDisp(code))+" pid "+pr.Id+": applied "+applied+", already-ok "+already+(missing>0?(", missing "+missing):"")+".");
                done.Add(pr.Id);
            }
            finally{ CloseHandle(h); }
        }

        void Log(string m){ string line=DateTime.Now.ToString("HH:mm:ss")+"  "+m+"\r\n";
            if(txtLog.InvokeRequired) txtLog.BeginInvoke((Action)(()=>{ txtLog.AppendText(line); })); else txtLog.AppendText(line); }

        void LoadCfg(){ try{ if(!File.Exists(cfg)) { SuggestDefaults(); return; }
            foreach(var ln in File.ReadAllLines(cfg)){ int i=ln.IndexOf('='); if(i<1) continue; string k=ln.Substring(0,i).Trim(), val=ln.Substring(i+1).Trim();
                if(k=="version"){ if(val=="v113") { rbV113.Checked=true; } else { rb2000.Checked=true; } }
                else if(k=="exe") txtExe.Text=val; else if(k=="gpx") txtGpx.Text=val; }
            if(txtGpx.Text.Length==0) SuggestGpx(); }catch{ SuggestDefaults(); } }
        void SuggestDefaults(){ if(rbV113.Checked && File.Exists(@"D:\gp3\GP3.exe")) txtExe.Text=@"D:\gp3\GP3.exe";
            else if(File.Exists(@"D:\gp32k\gp3_2000.exe")) txtExe.Text=@"D:\gp32k\gp3_2000.exe"; SuggestGpx(); }
        void SuggestGpx(){ foreach(var g in new[]{ Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GPxPatch.exe"), @"D:\gp3\GPxPatch.exe" }) if(File.Exists(g)){ txtGpx.Text=g; break; } }
        static string AsmVer(){ var v=Assembly.GetExecutingAssembly().GetName().Version; return v.Major+"."+v.Minor; }
        static string AttrCopyright(){ var a=(AssemblyCopyrightAttribute[])Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute),false); return a.Length>0?a[0].Copyright:"Copyright © James Knopp 2026"; }
        static string AttrDesc(){ var a=(AssemblyDescriptionAttribute[])Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute),false); return a.Length>0?a[0].Description:""; }
        void ShowAbout(){
            string ver=Assembly.GetExecutingAssembly().GetName().Version.ToString();
            using(var f=new Form{ Text="About GP3 Tyre Fix Injector", FormBorderStyle=FormBorderStyle.FixedDialog, StartPosition=FormStartPosition.CenterParent, ClientSize=new Size(440,262), MaximizeBox=false, MinimizeBox=false, ShowInTaskbar=false, Font=this.Font }){
                f.Controls.Add(new Label{ Text="GP3 Tyre Fix Injector", AutoSize=true, Location=new Point(16,16), Font=new Font("Segoe UI",12.5f,FontStyle.Bold) });
                f.Controls.Add(new Label{ Text="Version "+ver, AutoSize=true, Location=new Point(18,46), ForeColor=SystemColors.GrayText });
                f.Controls.Add(new Label{ Text=AttrDesc(), Location=new Point(18,76), Size=new Size(404,98) });
                f.Controls.Add(new Label{ Text=AttrCopyright(), AutoSize=true, Location=new Point(18,182) });
                var ok=new Button{ Text="OK", DialogResult=DialogResult.OK, Size=new Size(86,28), Location=new Point(338,220) };
                f.Controls.Add(ok); f.AcceptButton=ok; f.CancelButton=ok; f.ShowDialog(this);
            }
        }
        void SaveCfg(){ try{ File.WriteAllText(cfg, "version="+(selV113?"v113":"2000")+"\r\nexe="+txtExe.Text.Trim()+"\r\ngpx="+txtGpx.Text.Trim()+"\r\n"); }catch{} }
    }
}
