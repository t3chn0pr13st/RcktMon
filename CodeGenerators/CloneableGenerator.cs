using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace CodeGenerators
{
    [Generator]
    public class CloneableGenerator : ISourceGenerator
    {
        internal const string AttributeName = "Cloneable";
        internal const string AttributeNamespace = "CoreCodeGenerators";

        private string _attributeText = $@"
using System;
namespace {AttributeNamespace}
{{
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class {AttributeName}Attribute : Attribute
    {{
        public {AttributeName}Attribute()
        {{

        }}
    }}
}}
";

        public void Execute( GeneratorExecutionContext context )
        {
            context.AddSource("cloneableAttribute", SourceText.From(_attributeText, Encoding.UTF8));

            if (!(context.SyntaxReceiver is CloneableSyntaxReceiver receiver))
                return;

            CSharpParseOptions options = (CSharpParseOptions)((CSharpCompilation)context.Compilation).SyntaxTrees[0].Options;
            Compilation compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(_attributeText, Encoding.UTF8), options));

            foreach (var classDeclaration in receiver.CandidateClasses )
            {
               var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
               var classTypeSymbol = model.GetDeclaredSymbol(classDeclaration);
               if (classTypeSymbol == null)
                    throw new InvalidOperationException("ClassTypeSymbol not found");
               var code = Generate(classDeclaration, classTypeSymbol.ContainingNamespace.ToDisplayString());
               context.AddSource($"{classDeclaration.Identifier}_cl.g.cs", SourceText.From(code, Encoding.UTF8));
            }

        }

        private string Generate(ClassDeclarationSyntax c, string ns)
        {
            var sb = new StringBuilder();

            foreach (var p in c.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (p.AccessorList?.Accessors.Any(a => a.Keyword.ValueText == "set") != true)
                    continue;
                var propName = p.Identifier.ToString();
                sb.AppendLine($@"
                newObj.{propName} = this.{propName};");
            }

            return @$"
using System;
using System.ComponentModel;

namespace {ns}
{{
    partial class {c.Identifier}
    {{
        public virtual object Clone()
        {{
            var newObj = new {c.Identifier}();
{sb}
            return newObj;
        }}
    }}
}}
";
        }

        public void Initialize( GeneratorInitializationContext context )
        {
            context.RegisterForSyntaxNotifications(() => new CloneableSyntaxReceiver());
        }
    }

    class CloneableSyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> CandidateClasses { get; } = new List<ClassDeclarationSyntax>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax
                && classDeclarationSyntax.AttributeLists.Count > 0)
            {
                var attributeSyntax = classDeclarationSyntax.AttributeLists
                    .SelectMany(a => a.Attributes)
                    .SingleOrDefault(a => a.Name is 
                    IdentifierNameSyntax { Identifier: { ValueText: CloneableGenerator.AttributeName } } 
                    or
                    QualifiedNameSyntax {
                        Left: IdentifierNameSyntax { Identifier: { ValueText: CloneableGenerator.AttributeNamespace }},
                        Right: IdentifierNameSyntax { Identifier: { ValueText: CloneableGenerator.AttributeName }}
                    });

                if (attributeSyntax != null)
                    CandidateClasses.Add(classDeclarationSyntax);
            }                
        }
    }
}
