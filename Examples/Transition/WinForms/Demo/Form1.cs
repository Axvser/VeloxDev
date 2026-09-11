using System.Windows.Forms;

namespace Demo
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            // 过冲演示区在运行时构建（见 Form1.Overshoot.cs），设计器文件保持原样
            InitializeOvershootStrip();
        }
    }
}
