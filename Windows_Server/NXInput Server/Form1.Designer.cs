namespace NXInput_Server
{
    partial class Form1
    {
        /// <summary>
        /// Variabile di progettazione necessaria.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Pulire le risorse in uso.
        /// </summary>
        /// <param name="disposing">ha valore true se le risorse gestite devono essere eliminate, false in caso contrario.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Codice generato da Progettazione Windows Form

        /// <summary>
        /// Metodo necessario per il supporto della finestra di progettazione. Non modificare
        /// il contenuto del metodo con l'editor di codice.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.adapterComboBox = new System.Windows.Forms.ComboBox();
            this.refreshButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(104, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = L("选择与 Switch 通信的网卡 / IPv4 地址","Select the network adapter / IPv4 address for the Switch");
            this.adapterComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.adapterComboBox.Location = new System.Drawing.Point(15, 32);
            this.adapterComboBox.Size = new System.Drawing.Size(620, 24);
            this.adapterComboBox.DropDownWidth = 720;
            this.adapterComboBox.SelectedIndexChanged += (sender, args) => this.UpdateSelectedAdapter();
            this.refreshButton.Location = new System.Drawing.Point(645, 30);
            this.refreshButton.Size = new System.Drawing.Size(90, 28);
            this.refreshButton.Text = L("刷新网卡","Refresh");
            this.refreshButton.Click += (sender, args) => this.RefreshNetworkAdapters();
            //
            // label2
            //
            this.label2.AutoSize = false;
            this.label2.Location = new System.Drawing.Point(15, 68);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(720, 65);
            this.label2.TabIndex = 1;
            this.label2.Text = L("请选择网卡。","Select a network adapter.");
            //
            // button1
            //
            this.button1.Location = new System.Drawing.Point(645, 140);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(90, 30);
            this.button1.TabIndex = 2;
            this.button1.Text = L("启动服务","Start server");
            this.button1.Enabled = false;
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            //
            // richTextBox1
            //
            this.richTextBox1.Location = new System.Drawing.Point(15, 180);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.ForcedVertical;
            this.richTextBox1.Size = new System.Drawing.Size(720, 185);
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.TabIndex = 3;
            this.richTextBox1.Text = "";
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(750, 380);
            this.Controls.Add(this.adapterComboBox);
            this.Controls.Add(this.refreshButton);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.Text = "Lite Link 0.2.0";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.ComboBox adapterComboBox;
        private System.Windows.Forms.Button refreshButton;
    }
}

