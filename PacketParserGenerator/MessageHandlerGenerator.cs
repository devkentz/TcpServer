using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PacketParserGenerator
{
    /// <summary>
    /// 
    /// </summary>
    [Generator]
    public class ActorMessageHandlerGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 2. [NodeController]가 붙은 클래스 찾기
            var controllerClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (s, _) => IsCandidateClass(s),
                    transform: (ctx, _) => GetControllerModel(ctx))
                .Where(m => m != null);

            // 3. 소스 생성 실행
            context.RegisterSourceOutput(controllerClasses.Collect(), Execute);
        }

        private bool IsCandidateClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0;
        }

        private ControllerModel GetControllerModel(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null) return null;

            var attributes = symbol.GetAttributes();
            if (!attributes.Any(a => a.AttributeClass?.Name == "ServerControllerAttribute"))
                return null;

            var methods = new List<HandlerMethod>();

            foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                var attr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "PacketHandlerAttribute");
                if (attr != null && attr.ConstructorArguments.Length > 0)
                {
                    var msgId = (long)attr.ConstructorArguments[0].Value!;
                    
                    bool isActorHandler = false;
                    string requestType = "";

                    if (member.Parameters.Length == 1)
                    {
                        // Handler(Request req)
                        requestType = member.Parameters[0].Type.ToDisplayString();
                        isActorHandler = false;
                    }
                    else if (member.Parameters.Length == 2 && member.Parameters[0].Type.Name == "IActor")
                    {
                        // Handler(IActor actor, Request req)
                        requestType = member.Parameters[1].Type.ToDisplayString();
                        isActorHandler = true;
                    }
                    else
                    {
                        continue; // Invalid signature
                    }
                    
                    var returnTypeSymbol = member.ReturnType as INamedTypeSymbol;
                    string responseType = "Google.Protobuf.IMessage";

                    if (returnTypeSymbol != null && returnTypeSymbol.Name == "Task" && returnTypeSymbol.IsGenericType)
                    {
                        responseType = returnTypeSymbol.TypeArguments[0].ToDisplayString();
                    }

                    methods.Add(new HandlerMethod(member.Name, msgId, requestType, responseType, isActorHandler));
                }
            }

            return new ControllerModel(symbol.ContainingNamespace.ToDisplayString(), symbol.Name, methods);
        }

        private void Execute(SourceProductionContext context, ImmutableArray<ControllerModel> controllers)
        {
            if (controllers.IsDefaultOrEmpty) return;

            var sb = new StringBuilder();
            
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Google.Protobuf;");
            sb.AppendLine("using Network.Server.Tcp.Actor;");
            sb.AppendLine("using Network.Server.Tcp.Core;");
            
            sb.AppendLine(@"
namespace Network.Server.Generated
{
    public class GeneratedMessageHandler : MessageHandler
    {
        protected override void LoadHandlers()
        {");

            foreach (var controller in controllers)
            {
                if (controller == null) continue;

                foreach (var method in controller.Methods)
                {
                    if (method.IsActorHandler)
                    {
                        sb.AppendLine($@"            AddHandler<{method.RequestType}>({method.RequestType}.MsgId, (provider, actor, req) => provider.GetRequiredService<{controller.Namespace}.{controller.ClassName}>().{method.Name}(actor, req));");
                    }
                    else
                    {
                        sb.AppendLine($@"            AddHandler<{method.RequestType}>({method.RequestType}.MsgId, (provider, req) => provider.GetRequiredService<{controller.Namespace}.{controller.ClassName}>().{method.Name}(req));");
                    }
                }
            }

            sb.AppendLine(@"        }
    }
}");

            context.AddSource("MessageHandler.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private class ControllerModel
        {
            public string Namespace { get; }
            public string ClassName { get; }
            public List<HandlerMethod> Methods { get; }

            public ControllerModel(string @namespace, string className, List<HandlerMethod> methods)
            {
                Namespace = @namespace;
                ClassName = className;
                Methods = methods;
            }
        }

        private class HandlerMethod
        {
            public string Name { get; }
            public long MsgId { get; }
            public string RequestType { get; }
            public string ResponseType { get; }
            public bool IsActorHandler { get; }

            public HandlerMethod(string name, long msgId, string requestType, string responseType, bool isActorHandler)
            {
                Name = name;
                MsgId = msgId;
                RequestType = requestType;
                ResponseType = responseType;
                IsActorHandler = isActorHandler;
            }
        }
    }
}