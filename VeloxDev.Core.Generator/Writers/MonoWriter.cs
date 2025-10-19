using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Writers
{
    public class MonoWriter : WriterBase
    {
        public bool IsMono { get; private set; } = false;
        public int MonoSpan { get; private set; } = 17;

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
            return IsMono;
        }

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_VeloxMono.g.cs";
        }

        public override string Write()
        {
            if (!CanWrite()) return string.Empty;

            StringBuilder builder = new();
            builder.AppendLine(GenerateHead());
            builder.AppendLine(GeneratePartialClass(GenerateMonoBehaviour()));

            return builder.ToString();
        }

        private string GeneratePartialClass(string body)
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            StringBuilder sourceBuilder = new();
            string classDeclaration = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var source = $$"""
                           {{classDeclaration}}
                           {
                           {{body}}
                           }
                           """;
            sourceBuilder.AppendLine(source);

            return sourceBuilder.ToString();
        }

        private string GenerateMonoBehaviour()
        {
            StringBuilder builder = new();

            builder.AppendLine($$"""
                                        private global::System.Threading.CancellationTokenSource? _monoCancellationTokenSource = null;

                                        private bool _isMonoBehaviourEnabled = false;
                                        public bool IsMonoBehaviourEnabled
                                        {
                                            get => _isMonoBehaviourEnabled;
                                            set
                                            {
                                                if(_isMonoBehaviourEnabled != value)
                                                {
                                                    _isMonoBehaviourEnabled = value;
                                                    if (value)
                                                    {
                                                        StartMonoBehaviour();
                                                    }
                                                    else
                                                    {
                                                        StopMonoBehaviour();
                                                    }
                                                }
                                            }
                                        }

                                        /// <summary>
                                        /// 启动MonoBehaviour循环
                                        /// </summary>
                                        public void StartMonoBehaviour()
                                        {
                                            StopMonoBehaviour();

                                            _isMonoBehaviourEnabled = true;
                                            var cancellationTokenSource = new global::System.Threading.CancellationTokenSource();
                                            _monoCancellationTokenSource = cancellationTokenSource;

                                            // 启动异步循环
                                            var monoTask = global::System.Threading.Tasks.Task.Run(async () =>
                                            {
                                                await RunMonoBehaviourLoop(cancellationTokenSource.Token);
                                            }, cancellationTokenSource.Token);
                                        }

                                        /// <summary>
                                        /// 停止MonoBehaviour循环
                                        /// </summary>
                                        public void StopMonoBehaviour()
                                        {
                                            _isMonoBehaviourEnabled = false;
                                            
                                            var oldCts = _monoCancellationTokenSource;
                                            if (oldCts != null)
                                            {
                                                try 
                                                { 
                                                    oldCts.Cancel(); 
                                                    oldCts.Dispose(); 
                                                } 
                                                catch { }
                                                _monoCancellationTokenSource = null;
                                            }
                                        }

                                        private async global::System.Threading.Tasks.Task RunMonoBehaviourLoop(global::System.Threading.CancellationToken cancellationToken)
                                        {
                                            try
                                            {
                                                // 调用Start方法
                                                OnMonoStart();

                                                while (_isMonoBehaviourEnabled && !cancellationToken.IsCancellationRequested)
                                                {
                                                    // 调用Update方法
                                                    OnMonoUpdate();
                                                    
                                                    // 调用LateUpdate方法
                                                    OnMonoLateUpdate();
                                                    
                                                    // 等待指定的间隔
                                                    await global::System.Threading.Tasks.Task.Delay(MonoSpan, cancellationToken);
                                                }
                                            }
                                            catch (global::System.OperationCanceledException)
                                            {
                                                // 正常取消，忽略异常
                                            }
                                            catch (global::System.Exception ex)
                                            {
                                                global::System.Diagnostics.Debug.WriteLine($"[MonoBehaviour] Error: {ex.Message}");
                                            }
                                            finally
                                            {
                                                OnMonoExit();
                                                StopMonoBehaviour();
                                            }
                                        }

                                        /// <summary>
                                        /// MonoBehaviour开始时的回调方法
                                        /// </summary>
                                        protected virtual void OnMonoStart()
                                        {
                                            MonoStart();
                                        }

                                        /// <summary>
                                        /// MonoBehaviour每帧更新的回调方法
                                        /// </summary>
                                        protected virtual void OnMonoUpdate()
                                        {
                                            MonoUpdate();
                                        }

                                        /// <summary>
                                        /// MonoBehaviour每帧后期更新的回调方法
                                        /// </summary>
                                        protected virtual void OnMonoLateUpdate()
                                        {
                                            MonoLateUpdate();
                                        }

                                        /// <summary>
                                        /// MonoBehaviour退出时的回调方法
                                        /// </summary>
                                        protected virtual void OnMonoExit()
                                        {
                                            MonoExit();
                                        }

                                        #region 部分方法 - 由派生类实现

                                        /// <summary>
                                        /// 当MonoBehaviour开始时调用（部分方法）
                                        /// </summary>
                                        partial void MonoStart();

                                        /// <summary>
                                        /// 当MonoBehaviour每帧更新时调用（部分方法）
                                        /// </summary>
                                        partial void MonoUpdate();

                                        /// <summary>
                                        /// 当MonoBehaviour每帧后期更新时调用（部分方法）
                                        /// </summary>
                                        partial void MonoLateUpdate();

                                        /// <summary>
                                        /// 当MonoBehaviour退出时调用（部分方法）
                                        /// </summary>
                                        partial void MonoExit();

                                        #endregion

                                     """);

            return builder.ToString();
        }
    }
}