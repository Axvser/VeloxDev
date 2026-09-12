using System.Windows.Forms;

namespace Demo
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            // 顶栏、读数与案例列表在运行时构建（见 Form1.Overshoot.cs），设计器文件只当零件库用
            BuildBench();
        }
    }
}
