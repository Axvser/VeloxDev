using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace VeloxDev.Core.Generator.Writers
{
    public class MonoWriter : WriterBase
    {
        private bool IsMono { get; set; } = false;
        private int MonoSpan { get; set; } = 17;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadMonoConfig(namedTypeSymbol);
        }

        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad =>
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    NAMESPACE_VELOX_TIMELINE + ".MonoBehaviourAttribute" &&
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

        public override bool CanWrite() => IsMono;

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Mono.g.cs";
        }

        public override string GenerateBody()
        {
            if (Syntax == null || Symbol == null || !IsMono)
            {
                return string.Empty;
            }

            return $$"""
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
            """;
        }

        public override string[] GenerateBaseTypes() => [];
        public override string[] GenerateBaseInterfaces() => [];
    }
}