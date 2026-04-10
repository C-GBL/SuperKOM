using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using KOM_DUMP_MARCH.KomLib;

// Written by StaticCG25 for the lovely users on RAGEZONE!

namespace KOM_DUMP_MARCH
{
    public partial class MainForm : Form
    {
        private bool _dragOver;

        public MainForm()
        {
            InitializeComponent();
        }

        //  Drag-and-drop 

        private void DropPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            bool valid = false;
            foreach (var p in paths)
            {
                if (File.Exists(p) && p.EndsWith(".kom", StringComparison.OrdinalIgnoreCase)) { valid = true; break; }
                if (Directory.Exists(p)) { valid = true; break; }
            }
            e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
            _dragOver = valid;
            dropPanel.Invalidate();
        }

        private void DropPanel_DragLeave(object sender, EventArgs e)
        {
            _dragOver = false;
            dropPanel.Invalidate();
        }

        private void DropPanel_DragDrop(object sender, DragEventArgs e)
        {
            _dragOver = false;
            dropPanel.Invalidate();
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var path in paths)
            {
                string captured = path;
                if (File.Exists(captured) && captured.EndsWith(".kom", StringComparison.OrdinalIgnoreCase))
                    Task.Run(() => DoExtract(captured));
                else if (Directory.Exists(captured))
                    Task.Run(() => DoPack(captured));
            }
        }

        private void DoExtract(string komPath)
        {
            string name = Path.GetFileName(komPath);
            try
            {
                Log($"Extracting {name}...");
                string outDir = Path.Combine(Path.GetDirectoryName(komPath),
                    Path.GetFileNameWithoutExtension(komPath));
                Directory.CreateDirectory(outDir);
                KomFile.Extract(komPath, outDir);
                Log($"Done  {name}  →  {Path.GetFileName(outDir)}{Path.DirectorySeparatorChar}");
            }
            catch (Exception ex)
            {
                Log($"Error [{name}]: {ex.Message}");
            }
        }

        private void DoPack(string folderPath)
        {
            string folderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
            string komPath = Path.Combine(Path.GetDirectoryName(folderPath.TrimEnd('\\', '/')), folderName + ".kom");
            try
            {
                Log($"Packing {folderName}{Path.DirectorySeparatorChar}  →  {folderName}.kom ...");
                KomFile.Pack(folderPath, komPath);
                Log($"Done  {folderName}.kom  ({new FileInfo(komPath).Length / 1024} KB)");
            }
            catch (Exception ex)
            {
                Log($"Error [{folderName}]: {ex.Message}");
            }
        }

        private void Log(string msg)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action(() => Log(msg)));
                return;
            }
            logBox.SelectionColor = Color.FromArgb(200, 200, 200);
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
            logBox.SelectionColor = Color.FromArgb(150, 220, 150);
            logBox.AppendText(msg + "\n");
            logBox.ScrollToCaret();
        }

        //  Drop-zone custom paint 

        private void DropPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = dropPanel.ClientRectangle;
            r.Inflate(-1, -1);

            Color borderColor = _dragOver
                ? Color.FromArgb(100, 200, 100)
                : Color.FromArgb(80, 80, 80);

            using (var pen = new Pen(borderColor, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawRectangle(pen, r);

            string line1 = "Drop  .KOM  files  to  extract";
            string line2 = "Drop  folders  to  repack  as  .KOM";

            using (var fBig = new Font("Segoe UI", 13f, FontStyle.Regular))
            using (var fSmall = new Font("Segoe UI", 10f, FontStyle.Regular))
            using (var br1 = new SolidBrush(Color.FromArgb(210, 210, 210)))
            using (var br2 = new SolidBrush(Color.FromArgb(130, 130, 130)))
            {
                SizeF s1 = g.MeasureString(line1, fBig);
                SizeF s2 = g.MeasureString(line2, fSmall);

                float totalH = s1.Height + 6 + s2.Height;
                float y1 = (r.Height - totalH) / 2f;

                g.DrawString(line1, fBig, br1, (r.Width - s1.Width) / 2f, y1);
                g.DrawString(line2, fSmall, br2, (r.Width - s2.Width) / 2f, y1 + s1.Height + 6);
            }
        }

        private void label1_Click(object sender, EventArgs e)
        {
            Process.Start("https://forum.ragezone.com/members/staticcg25.2000523638/");
        }
    }
}
