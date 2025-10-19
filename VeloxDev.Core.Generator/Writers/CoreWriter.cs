using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.Core.Generator.Base;
using VeloxDev.Core.Generator.Templates.Workflow;

namespace VeloxDev.Core.Generator.Writers
{
    public class CoreWriter : WriterBase
    {
        public bool IsMVVM { get; set; } = false;
        public bool IsAop { get; set; } = false;
        public bool IsMono { get; set; } = false;
        public int MonoSpan { get; set; } = 17;
        public int WorkflowType { get; set; } = 0;
        List<Analizer.MVVMPropertyFactory> MVVMProperties { get; set; } = [];

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
            ReadMonoConfig(namedTypeSymbol);
            ReadMVVMConfig(namedTypeSymbol);
        }

        private void ReadMVVMConfig(INamedTypeSymbol symbol)
        {
            MVVMProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                        NAMESPACE_VELOX_MVVM + ".VeloxPropertyAttribute"))
                    .Select(field => new Analizer.MVVMFieldAnalizer(field))
                    .Select(analizer => new Analizer.MVVMPropertyFactory(analizer, "public", false)
                    {
                        SetteringBody = [$"OnPropertyChanging(nameof({analizer.PropertyName}));"],
                        SetteredBody = [$"OnPropertyChanged(nameof({analizer.PropertyName}));"],
                    })
            ];
            IsMVVM = MVVMProperties.Count > 0 || WorkflowType != 0;
        }

        private void ReadAopConfig(ClassDeclarationSyntax classDeclaration)
        {
            IsAop = classDeclaration.Members
                .OfType<MemberDeclarationSyntax>()
                .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == AnalizeHelper.NAME_ASPECTORIENTED));
        }

        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    NAMESPACE_VELOX_MONO + ".MonoBehaviourAttribute" &&
                    ad.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax &&
                    attrSyntax.Parent?.Parent is ClassDeclarationSyntax
                );

            IsMono = attributeData != null;
            if (IsMono)
            {
                var value = (int)attributeData!.ConstructorArguments[0].Value!;
                MonoSpan = (int)(1000d / value > 0 ? value : 1);
            }
        }

        public override bool CanWrite()
        {
            return IsAop || IsMono || IsMVVM || WorkflowType != 0;
        }

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return
                $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_VeloxCore.g.cs";
        }

        public override string Write()
        {
            StringBuilder builder = new();
            builder.AppendLine(GeneratePartial(GenerateBody()));

            return builder.ToString();
        }

        private string GeneratePartial(string body)
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();

            if (IsAop)
            {
                list.Add(
                    $"{NAMESPACE_VELOX_AOP}.{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop");
            }

            if (IsMVVM && WorkflowType == 0)
            {
                list.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanging");
                list.Add($"{NAMESPACE_SYSTEM_MVVM}.INotifyPropertyChanged");
            }

            switch (WorkflowType)
            {
                case 1:
                    list.Add(NAMESPACE_VELOX_IWORKFLOW + ".IWorkflowTree");
                    break;
                case 2:
                    list.Add(NAMESPACE_VELOX_IWORKFLOW + ".IWorkflowNode");
                    break;
                case 3:
                    list.Add(NAMESPACE_VELOX_IWORKFLOW + ".IWorkflowSlot");
                    break;
                case 4:
                    list.Add(NAMESPACE_VELOX_IWORKFLOW + ".IWorkflowLink");
                    break;
            }

            if (list.Count > 0)
            {
                var result = string.Join(", ", list);
                var source = $$"""
                               {{share}} : {{result}}
                               {
                               """;
                sourceBuilder.AppendLine(source);
            }
            else
            {
                var source = $$"""
                               {{share}}
                               {
                               """;
                sourceBuilder.AppendLine(source);
            }

            sourceBuilder.AppendLine(body);
            sourceBuilder.AppendLine("}");

            return sourceBuilder.ToString();
        }

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            var strAop =
                $"{NAMESPACE_VELOX_AOP}.{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                        private {{strAop}}? _proxy = null;
                                        public {{strAop}} Proxy
                                        {
                                            get
                                            {
                                                if (_proxy == null)
                                                {
                                                    var newproxy = global::VeloxDev.Core.AspectOriented.ProxyEx.CreateProxy<{{strAop}}>(this);
                                                    _proxy = newproxy;
                                                    return newproxy;
                                                }
                                                return _proxy;
                                            }
                                        }
                                     """);
                builder.AppendLine();
            }

            if (IsMono)
            {
                builder.AppendLine($$"""
                                        private global::System.Threading.CancellationTokenSource? cts_mono = null;

                                        private bool _canmonobehaviour = false;
                                        public bool CanMonoBehaviour
                                        {
                                            get => _canmonobehaviour;
                                            set
                                            {
                                                if(_canmonobehaviour != value)
                                                {
                                                    _canmonobehaviour = value;
                                                    if (value)
                                                    {
                                                        var monofunc = new global::System.Func<global::System.Threading.Tasks.Task>(async () =>
                                                        {
                                                            await _inner_Update();
                                                        });
                                                        monofunc?.Invoke();
                                                    }
                                                    else
                                                    {
                                                        _innerCleanMonoToken();
                                                    }
                                                }
                                            }
                                        }

                                        private async global::System.Threading.Tasks.Task _inner_Update()
                                        {
                                            _innerCleanMonoToken();

                                            var newmonocts = new global::System.Threading.CancellationTokenSource();
                                            cts_mono = newmonocts;

                                            try
                                            {
                                               if(CanMonoBehaviour) Start();

                                               while (CanMonoBehaviour && !newmonocts.Token.IsCancellationRequested)
                                               {
                                                   Update();
                                                   LateUpdate();
                                                   await global::System.Threading.Tasks.Task.Delay({{MonoSpan}},newmonocts.Token);
                                               }
                                            }
                                            catch (global::System.Exception ex)
                                            {
                                                global::System.Diagnostics.Debug.WriteLine(ex.Message);
                                            }
                                            finally
                                            {
                                                if (global::System.Threading.Interlocked.CompareExchange(ref cts_mono, null, newmonocts) == newmonocts) 
                                                {
                                                    newmonocts.Dispose();
                                                }
                                                ExitMonoBehaviour();
                                            }
                                        }

                                        partial void Start();
                                        partial void Update();
                                        partial void LateUpdate();
                                        partial void ExitMonoBehaviour();

                                        private void _innerCleanMonoToken()
                                        {
                                            var oldCts = global::System.Threading.Interlocked.Exchange(ref cts_mono, null);
                                            if (oldCts != null)
                                            {
                                                try { oldCts.Cancel(); } catch { }
                                                oldCts.Dispose();
                                            }
                                        }

                                     """);
            }

            if (IsMVVM)
            {
                builder.AppendLine($$"""
                                        public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventHandler? PropertyChanging;
                                        public event {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventHandler? PropertyChanged;
                                        public void OnPropertyChanging(string propertyName)
                                        {
                                            PropertyChanging?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangingEventArgs(propertyName));
                                        }
                                        public void OnPropertyChanged(string propertyName)
                                        {
                                            PropertyChanged?.Invoke(this, new {{NAMESPACE_SYSTEM_MVVM}}.PropertyChangedEventArgs(propertyName));
                                        }
                                     """);
                builder.AppendLine(GenerateProperty());
            }

            return builder.ToString();
        }

        private string GenerateProperty()
        {
            StringBuilder builder = new();
            foreach (var factory in MVVMProperties)
            {
                builder.AppendLine(factory.Generate());
            }

            return builder.ToString();
        }
    }
}