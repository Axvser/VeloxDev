using Newtonsoft.Json;
using System.IO;
using System.Windows;
using WpfApp2.ViewModels;

namespace WpfApp2
{
    public partial class MainWindow : Window
    {
        private readonly JsonSerializerSettings settings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto, // 允许接口与抽象类
            NullValueHandling = NullValueHandling.Include, // 包含空值
            Formatting = Formatting.Indented, // 格式对齐
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore, // 忽略循环引用
            PreserveReferencesHandling = PreserveReferencesHandling.Objects, // 保留对象引用
        };

        FactoryViewModel? fc;
        public MainWindow()
        {
            InitializeComponent();
            string json = File.ReadAllText(@"E:\\tree.json");
            var result = JsonConvert.DeserializeObject<FactoryViewModel>(json, settings);
            container.DataContext = result;
            fc = result;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            fc?.UndoCommand.Execute(null);
        }
    }
}