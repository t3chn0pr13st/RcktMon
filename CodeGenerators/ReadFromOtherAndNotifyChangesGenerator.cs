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
    public class ReadFromOtherAndNotifyChangesGenerator : ISourceGenerator
    {
        internal const string AttributeName = "ReadFromOtherAndNotify";
        internal const string AttributeNamespace = "CoreCodeGenerators";

        private string _attributeText = $@"
using System;
namespace {AttributeNamespace}
{{
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class {AttributeName}Attribute : Attribute
    {{
        public ReadFromOtherAndNotifyAttribute()
        {{

        }}
    }}
}}
";

        public void Execute( GeneratorExecutionContext context )
        {
            context.AddSource("readFromOtherAndNotifyAttribute", SourceText.From(_attributeText, Encoding.UTF8));

            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            CSharpParseOptions options = (context.Compilation as CSharpCompilation).SyntaxTrees[0].Options as CSharpParseOptions;
            Compilation compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(_attributeText, Encoding.UTF8), options));

            foreach (var classDeclaration in receiver.CandidateClasses )
            {
               var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
               var classTypeSymbol = model.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
               var code = Generate(classDeclaration, classTypeSymbol.ContainingNamespace.ToDisplayString());
               context.AddSource($"{classDeclaration.Identifier}_rf.g.cs", SourceText.From(code, Encoding.UTF8));
            }

        }

        private string Generate(ClassDeclarationSyntax c, string ns)
        {
            var sb = new StringBuilder();

            foreach (var p in c.Members.OfType<PropertyDeclarationSyntax>())
            {
                var propName = p.Identifier.ToString();
                sb.Append($@"

            if (this.{propName} != other.{propName}) 
            {{
                this.{propName} = other.{propName};
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propName})));
            }}
            
                ");
            }

            return @$"
using System;
using System.ComponentModel;

namespace {ns}
{{
    partial class {c.Identifier.ToString()} : INotifyPropertyChanged
    {{
        public event PropertyChangedEventHandler PropertyChanged;

        public void ReadFrom({c.Identifier.ToString()} other)
        {{
            {sb}
        }}
    }}
}}
";
        }

        public void Initialize( GeneratorInitializationContext context )
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }
    }

    class SyntaxReceiver : ISyntaxReceiver
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
                    IdentifierNameSyntax { Identifier: { ValueText: ReadFromOtherAndNotifyChangesGenerator.AttributeName } } 
                    or
                    QualifiedNameSyntax {
                        Left: IdentifierNameSyntax { Identifier: { ValueText: ReadFromOtherAndNotifyChangesGenerator.AttributeNamespace }},
                        Right: IdentifierNameSyntax { Identifier: { ValueText: ReadFromOtherAndNotifyChangesGenerator.AttributeName }}
                    });

                if (attributeSyntax != null)
                    CandidateClasses.Add(classDeclarationSyntax);
            }                
        }
    }
}
