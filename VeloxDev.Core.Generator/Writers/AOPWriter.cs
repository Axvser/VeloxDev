using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Generator.Writers
{
    public class AOPWriter : WriterBase
    {
        public bool IsAop { get; private set; } = false;

        public override void Initialize(ClassDeclarationSyntax classDeclaration, INamedTypeSymbol namedTypeSymbol)
        {
            base.Initialize(classDeclaration, namedTypeSymbol);
            ReadAopConfig(classDeclaration);
        }

        private void ReadAopConfig(ClassDeclarationSyntax classDeclaration)
        {
            IsAop = classDeclaration.Members
                .OfType<MemberDeclarationSyntax>()
                .Any(member => member.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(attr => attr.Name.ToString() == "AspectOriented"));
        }

        public override bool CanWrite()
        {
            return IsAop;
        }

        public override string GetFileName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_VeloxAOP.g.cs";
        }

        public override string Write()
        {
            if (!CanWrite()) return string.Empty;

            StringBuilder builder = new();
            builder.AppendLine(GenerateHead());
            builder.AppendLine(GeneratePartialClass(GenerateAopProxy()));

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

            // 生成AOP接口实现
            var aopInterfaceName = GetAopInterfaceName();
            var interfaces = new[] { aopInterfaceName };

            if (interfaces.Length > 0)
            {
                var interfaceList = string.Join(", ", interfaces);
                var source = $$"""
                               {{classDeclaration}} : {{interfaceList}}
                               {
                               {{body}}
                               }
                               """;
                sourceBuilder.AppendLine(source);
            }
            else
            {
                var source = $$"""
                               {{classDeclaration}}
                               {
                               {{body}}
                               }
                               """;
                sourceBuilder.AppendLine(source);
            }

            return sourceBuilder.ToString();
        }

        private string GetAopInterfaceName()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            return $"{NAMESPACE_VELOX_AOP}.{Syntax.Identifier.Text}_{Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')}_Aop";
        }

        private string GenerateAopProxy()
        {
            if (Syntax == null || Symbol == null)
            {
                return string.Empty;
            }

            var aopInterfaceName = GetAopInterfaceName();

            StringBuilder builder = new();

            builder.AppendLine($$"""
                                        #region AOP代理实现

                                        private {{aopInterfaceName}}? _aopProxy = null;

                                        /// <summary>
                                        /// 获取AOP代理实例
                                        /// </summary>
                                        public {{aopInterfaceName}} AopProxy
                                        {
                                            get
                                            {
                                                if (_aopProxy == null)
                                                {
                                                    var newProxy = CreateAopProxy();
                                                    _aopProxy = newProxy;
                                                    return newProxy;
                                                }
                                                return _aopProxy;
                                            }
                                        }

                                        /// <summary>
                                        /// 创建AOP代理实例
                                        /// </summary>
                                        protected virtual {{aopInterfaceName}} CreateAopProxy()
                                        {
                                            return global::VeloxDev.Core.AspectOriented.ProxyEx.CreateProxy<{{aopInterfaceName}}>(this);
                                        }

                                        /// <summary>
                                        /// 清除AOP代理缓存（用于重新创建代理）
                                        /// </summary>
                                        public void ClearAopProxy()
                                        {
                                            _aopProxy = null;
                                        }

                                        #endregion

                                        #region 拦截器方法 - 供派生类重写

                                        /// <summary>
                                        /// 方法执行前拦截
                                        /// </summary>
                                        /// <param name="methodName">方法名</param>
                                        /// <param name="args">参数</param>
                                        protected virtual void OnBeforeMethodInvocation(string methodName, object?[] args)
                                        {
                                            // 派生类可以重写此方法来实现前置通知
                                        }

                                        /// <summary>
                                        /// 方法执行后拦截
                                        /// </summary>
                                        /// <param name="methodName">方法名</param>
                                        /// <param name="args">参数</param>
                                        /// <param name="result">执行结果</param>
                                        protected virtual void OnAfterMethodInvocation(string methodName, object?[] args, object? result)
                                        {
                                            // 派生类可以重写此方法来实现后置通知
                                        }

                                        /// <summary>
                                        /// 方法执行异常拦截
                                        /// </summary>
                                        /// <param name="methodName">方法名</param>
                                        /// <param name="args">参数</param>
                                        /// <param name="exception">异常</param>
                                        protected virtual void OnMethodInvocationException(string methodName, object?[] args, Exception exception)
                                        {
                                            // 派生类可以重写此方法来实现异常通知
                                        }

                                        #endregion

                                        #region 部分方法 - 由代码生成器实现

                                        /// <summary>
                                        /// AOP初始化完成时的回调
                                        /// </summary>
                                        partial void OnAopInitialized();

                                        #endregion

                                     """);

            return builder.ToString();
        }
    }
}