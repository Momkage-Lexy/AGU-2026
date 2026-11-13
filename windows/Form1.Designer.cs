namespace windows
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.SuspendLayout();

            // 
            // webView21
            // 
            this.webView21.CreationProperties = null;
            this.webView21.DefaultBackgroundColor = System.Drawing.Color.White;
            this.webView21.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webView21.Location = new System.Drawing.Point(0, 0);
            this.webView21.Name = "webView21";
            this.webView21.Size = new System.Drawing.Size(800, 450);
            this.webView21.TabIndex = 0;

            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.webView21);
            this.Name = "Form1";
            this.Text = "windows";
            this.ResumeLayout(false);

            this.exportBtn = new System.Windows.Forms.Button();
            this.exportBtn.Text = "Export CSV";
            this.exportBtn.Location = new System.Drawing.Point(10, 10);
            this.exportBtn.Click += (s, e) => ExportToCsv();
            this.Controls.Add(this.exportBtn);

        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private Button exportBtn;
    }
}