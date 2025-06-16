using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace VeloxDev.MAUI.Generator.Writers
{
    public class CoreWriter : WriterBase
    {
        public const string FULLNAME_MONOCONFIG = "global::VeloxDev.Core.Generators.MonoBehaviourAttribute";
        public const string NAMESPACE_AOP = "global::VeloxDev.Core.AopInterfaces.";

        public bool IsAop { get; set; } = false;
        public bool IsMono { get; set; } = false;
        public double MonoSpan { get; set; } = 17;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
            ReadMonoConfig(namedTypeSymbol);
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
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_MONOCONFIG &&
                    ad.ApplicationSyntaxReference != null // 确保是直接标记的特性
                );
            if (attributeData != null)
            {
                IsMono = true;
                var num = (int)attributeData.ConstructorArguments[0].Value!;
                MonoSpan = 1000d / num > 0 ? num : 1;
            }
        }

        public override bool CanWrite()
        {
            return IsAop || IsMono;
        }

        public override string GetFileName()
        {
            if (Syntax == null)
            {
                return string.Empty;
            }
            return $"{Syntax.Identifier.Text}_{AnalizeHelper.GetNamespace(Syntax).Replace('.', '_')}_VeloxCore.g.cs";
        }

        public override string Write()
        {
            StringBuilder builder = new();

            builder.AppendLine(GenerateHead());
            builder.AppendLine(GeneratePartial());
            builder.AppendLine(GenerateBody());
            builder.AppendLine(GenerateEnd());

            return builder.ToString();
        }

        private string GeneratePartial()
        {
            if (Syntax == null)
            {
                return string.Empty;
            }
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();

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

        private string GenerateBody()
        {
            if (Syntax == null)
            {
                return string.Empty;
            }
            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
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
            return builder.ToString();
        }
    }
}