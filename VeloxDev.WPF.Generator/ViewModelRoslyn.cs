using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VeloxDev.WPF.Generator.Factory;

namespace VeloxDev.WPF.Generator
{
    internal class ViewModelRoslyn : ClassRoslyn
    {
        private const string NAMESPACE_MVVM = "global::System.ComponentModel.";
        private const string NAMESPACE_MF = "global::VeloxDev.WPF.MessageFlow.";

        private const string FULLNAME_IMF = "global::VeloxDev.WPF.StructuralDesign.Message.IMessageFlow";
        private const string FULLNAME_SMF = "global::VeloxDev.WPF.SourceGeneratorMark.SubscribeMessageFlowsAttribute";
        const string PUBLIC = "public";

        internal ViewModelRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation) : base(classDeclarationSyntax, namedTypeSymbol, compilation)
        {
            var isvmatt = AnalizeHelper.IsViewModelClass(Symbol, out var vmfields);
            FieldRoslyns = vmfields.Select(field => new FieldRoslyn(field));
            TryReadSubscribedMessageFlows(namedTypeSymbol);
            IsMessageFlow = SubscribedMessageFlows.Count > 0;
            IsViewModel = isvmatt || IsMessageFlow;
        }

        public bool IsViewModel { get; set; } = false;
        public bool IsMessageFlow { get; set; } = false;

        public HashSet<string> SubscribedMessageFlows { get; set; } = [];

        public IEnumerable<FieldRoslyn> FieldRoslyns { get; set; } = [];

        private void TryReadSubscribedMessageFlows(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_SMF);
            if (attributeData != null)
            {
                foreach (var p in attributeData.ConstructorArguments[0].Values)
                {
                    if (p.Value is string str && !string.IsNullOrEmpty(str))
                    {
                        SubscribedMessageFlows.Add(str.Trim());
                    }
                }
            }
        }

        public string Generate()
        {
            var builder = new StringBuilder();

            builder.AppendLine(GenerateUsing());
            builder.AppendLine(GenerateNamespace());
            builder.AppendLine(GeneratePartialClass());
            builder.AppendLine(GenerateConstructor());
            builder.AppendLine(GenerateContext());
            builder.AppendLine(GenerateInitializeMinimalisticWPF());
            builder.AppendLine(GenerateIPC());
            builder.AppendLine(GenerateIMF());
            builder.AppendLine(GenerateEnd());

            return builder.ToString();
        }
        public string GeneratePartialClass()
        {
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();
            if (IsViewModel)
            {
                list.Add($"{NAMESPACE_MVVM}INotifyPropertyChanged");
                list.Add($"{NAMESPACE_MVVM}INotifyPropertyChanging");
            }
            if (IsMessageFlow)
            {
                list.Add(FULLNAME_IMF);
            }
            if (IsAop)
            {
                list.Add($"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}");
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

            return sourceBuilder.ToString();
        }
        public string GenerateIPC()
        {
            StringBuilder sourceBuilder = new();
            string source = $$"""
                                    public event {{NAMESPACE_MVVM}}PropertyChangedEventHandler? PropertyChanged;
                                    public event {{NAMESPACE_MVVM}}PropertyChangingEventHandler? PropertyChanging;
                                    public void OnPropertyChanging(string propertyName)
                                    {
                                       PropertyChanging?.Invoke(this, new {{NAMESPACE_MVVM}}PropertyChangingEventArgs(propertyName));
                                    }
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new {{NAMESPACE_MVVM}}PropertyChangedEventArgs(propertyName));
                                    }

                              """;
            sourceBuilder.AppendLine(source);

            foreach (var field in FieldRoslyns)
            {
                var factory = new PropertyFactory(field, PUBLIC, false);
                var intercept = field.SetterValidation switch
                {
                    1 => $"if ({field.FieldName} == value) return;",
                    2 => $"if (object.Equals({field.FieldName}, value)) return;",
                    3 => $"if (!CanUpdate{field.PropertyName}(old,value)) return;",
                    _ => string.Empty
                };
                var interceptmethod = field.SetterValidation switch
                {
                    3 => $"      private partial bool CanUpdate{field.PropertyName}({field.TypeName} oldValue, {field.TypeName} newValue);",
                    _ => string.Empty
                };
                factory.SetteringBody.Add(intercept);
                factory.SetteringBody.Add($"OnPropertyChanging(nameof({field.PropertyName}));");
                factory.SetteredBody.Add($"OnPropertyChanged(nameof({field.PropertyName}));");
                sourceBuilder.AppendLine(factory.Generate());
                sourceBuilder.AppendLine(interceptmethod);
                sourceBuilder.AppendLine();
            }

            return sourceBuilder.ToString();
        }
        public string GenerateConstructor()
        {
            if (!CanGenerateConstructor) return string.Empty;

            var acc = AnalizeHelper.GetAccessModifier(Symbol);

            var methods = Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(att => att.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_CONSTRUCTOR))
                .ToList();

            StringBuilder builder = new();

            builder.AppendLine($"      {acc} {Symbol.Name}()");
            builder.AppendLine("      {");
            builder.AppendLine("         InitializeMinimalisticWPF();");
            foreach (var method in methods.Where(m => !m.Parameters.Any()))
            {
                builder.AppendLine($"         {method.Name}();");
            }
            builder.AppendLine("      }");

            var groupedMethods = methods.Where(m => m.Parameters.Any()).GroupBy(m =>
                string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString())));

            foreach (var group in groupedMethods)
            {
                var parameters = group.Key.Split(',');
                var parameterList = string.Join(", ", group.First().Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
                var callParameters = string.Join(", ", group.First().Parameters.Select(p => p.Name));

                builder.AppendLine();
                builder.AppendLine($"      {acc} {Symbol.Name}({parameterList})");
                builder.AppendLine("      {");
                builder.AppendLine("         InitializeMinimalisticWPF();");
                foreach (var method in group)
                {
                    builder.AppendLine($"         {method.Name}({callParameters});");
                }
                builder.AppendLine("      }");
            }

            return builder.ToString();
        }
        public string GenerateContext()
        {
            var builder = new StringBuilder();

            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                           public required {{strAop}} Proxy { get; set; }
                                     """);
                builder.AppendLine();
            }

            if (IsMono)
            {
                builder.AppendLine($$"""
                          private global::System.Threading.CancellationTokenSource? cts_mono = null;

                          private bool _canmonobehaviour = true;
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
                                          var monofunc = new Func<Task>(async () =>
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

                          public async void SetCanMonoBehaviour(bool value)
                          {
                              if (value != CanMonoBehaviour)
                              {
                                  CanMonoBehaviour = value;
                                  if (value)
                                  {
                                      await _inner_Update();
                                  }
                                  else
                                  {
                                      _innerCleanMonoToken();
                                  }
                              }
                          }

                          private async Task _inner_Update()
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
                                     await Task.Delay({{MonoSpan}},newmonocts.Token);
                                 }
                              }
                              catch (Exception ex)
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

                          partial void Awake();
                          partial void Start();
                          partial void Update();
                          partial void LateUpdate();
                          partial void ExitMonoBehaviour();

                          private void _innerCleanMonoToken()
                          {
                              var oldCts = Interlocked.Exchange(ref cts_mono, null);
                              if (oldCts != null)
                              {
                                  try { oldCts.Cancel(); } catch { }
                                  oldCts.Dispose();
                              }
                          }

                    """);
            }

            return builder.ToString();
        }
        public string GenerateInitializeMinimalisticWPF()
        {
            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
            builder.AppendLine($"      private void InitializeMinimalisticWPF()");
            builder.AppendLine("      {");
            if (IsAop)
            {
                builder.AppendLine($"         Proxy = {NAMESPACE_PROXYEX}CreateProxy<{strAop}>(this);");
            }
            if (IsMono)
            {
                builder.AppendLine(GetMonoAwakeBody());
                builder.AppendLine(GetMonoUpdateBody());
            }
            foreach (var mfs in SubscribedMessageFlows)
            {
                builder.AppendLine($"         {NAMESPACE_MF}MessageCentral.SubscribeMessageFlow(\"{mfs}\", this);");
            }
            builder.AppendLine("      }");

            return builder.ToString();
        }
        public string GenerateIMF()
        {
            var builder = new StringBuilder();

            builder.AppendLine($$"""
                      public void SendMessageFlow(string name, params object?[] messages)
                      {
                          {{NAMESPACE_MF}}MessageCentral.SendMessage(this, name, messages);
                      }
                """);
            if (IsMessageFlow)
            {
                builder.AppendLine($$"""
                      public event {{NAMESPACE_MF}}MessageFlowHandler? MessageFlowRecieved;
                      public void RecieveMessageFlow(object sender, {{NAMESPACE_MF}}MessageFlowArgs e)
                      {
                          MessageFlowRecieved?.Invoke(sender, e);
                          {{NAMESPACE_MF}}MessageFlowHandler? handler = e.Name switch
                          {
                """);
                foreach (var mfs in SubscribedMessageFlows)
                {
                    builder.AppendLine($"              \"{mfs}\" => Flow{mfs},");
                }
                builder.AppendLine($$"""
                              _ => null
                          };
                          handler?.Invoke(sender, e);
                      }
                """);
                foreach (var mfs in SubscribedMessageFlows)
                {
                    builder.AppendLine($"      private partial void Flow{mfs}(object sender, {NAMESPACE_MF}MessageFlowArgs e);");
                }
            }

            return builder.ToString();
        }

        public override string GetMonoUpdateBody()
        {
            return IsMono switch
            {
                true => $$"""
                                   if(CanMonoBehaviour)
                                   {
                                       var monofunc = new Func<Task>(async () =>
                                       {
                                           await _inner_Update();
                                       });
                                       monofunc?.Invoke();
                                   }
                          """,
                _ => string.Empty
            };
        }
        public override string GetMonoAwakeBody()
        {
            return IsMono switch
            {
                true => "         Awake();",
                _ => string.Empty
            };
        }
    }
}
