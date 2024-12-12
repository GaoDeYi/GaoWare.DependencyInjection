using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.DotnetRuntime.Extensions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.Text;

namespace GaoWare.DependencyInjection.Generator;

public partial class DependencyInjectionGenerator
{
    private class CodeGenerator
    {
        public static readonly string DependencyRegistrationAttribute =
            "GaoWare.DependencyInjection.Attributes.DependencyRegistrationAttribute";

        private readonly SourceProductionContext _context;
        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;

        public CodeGenerator(SourceProductionContext context, Compilation compilation, CancellationToken cancellationToken)
        {
            _context = context;
            _compilation = compilation;
            _cancellationToken = cancellationToken;
        }

        public void GenerateOutput(IReadOnlyList<DependencyService> services, ImmutableArray<ClassDeclarationSyntax?> classDeclarations)
        {
            // HashSet to track fully qualified class names
            var processedClasses = new HashSet<string>();
            foreach (IGrouping<SyntaxTree?, ClassDeclarationSyntax?> group in classDeclarations.GroupBy(x => x?.SyntaxTree))
            {
                if (group?.Key is null)
                {
                    continue;
                }
                
                SyntaxTree syntaxTree = group.Key;
                SemanticModel sm = _compilation.GetSemanticModel(syntaxTree);

                foreach (var classDec in group)
                {
                    
                    if (classDec is not null)
                    {
                        var classSymbol = sm.GetDeclaredSymbol(classDec, _cancellationToken);
                        if (classSymbol is null || processedClasses.Any(q => q.Equals(classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))) continue;
                        
                        GenerateClassOutput(sm, classDec, services);
                        processedClasses.Add(classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                    
                }
            }
        }

        private void GenerateClassOutput(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration, IReadOnlyList<DependencyService> services)
        {
            INamedTypeSymbol? registrationAttribute =
                _compilation.GetBestTypeByMetadataName(DependencyRegistrationAttribute);
            if (registrationAttribute == null)
            {
                // nothing to do if this type isn't available
                return;
            }
            
            INamedTypeSymbol? iServiceCollectionType =
                _compilation.GetBestTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection");
            if (iServiceCollectionType == null)
            {
                // nothing to do if this type isn't available
                return;
            }
            
            // stop if we're asked to
            _cancellationToken.ThrowIfCancellationRequested();

            List<MemberDeclarationSyntax> members = new();

            foreach (var member in classDeclaration.Members)
            { 
                var method = member as MethodDeclarationSyntax;
                if (method == null)
                {
                    // we only care about methods
                    continue;
                }
                // Get symbol
                var methodSymbol = semanticModel.GetDeclaredSymbol(method, _cancellationToken);
                if (methodSymbol is null)
                {
                    continue;
                }

                // only work on methods with the attribute
                if (!methodSymbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, registrationAttribute)))
                {
                    continue;
                }
                
                var serviceCollectionParameter = methodSymbol.Parameters.FirstOrDefault(p =>
                    SymbolEqualityComparer.Default.Equals(p.Type, iServiceCollectionType));
                if (serviceCollectionParameter is null)
                {
                    continue;
                }
                
                // Add the statement
                List<StatementSyntax> statements = new();
                foreach (var service in services)
                {
                    var st = CreateStatement(serviceCollectionParameter, service);
                    if (st is not null) statements.Add(st);
                }

                if (!statements.Any())
                {
                    statements.Add(SyntaxFactory.ReturnStatement());
                }

                if (methodSymbol.ReturnsVoid)
                {
                    members.Add(
                        MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)),
                                Identifier(methodSymbol.Name))
                            .AddModifiers(method.Modifiers.ToArray())
                            .AddParameterListParameters(method.ParameterList.Parameters.ToArray())
                            .WithReturnType(method.ReturnType)
                            .AddBodyStatements(statements.ToArray())
                    );
                }
            }

            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, _cancellationToken);
            if (classSymbol is not null && members.Any())
            {
                var nullableEnable = SyntaxFactory.NullableDirectiveTrivia(
                    SyntaxFactory.Token(SyntaxKind.EnableKeyword),
                    true
                );
                // Create the nullable enable directive
                var nullableDisable = SyntaxFactory.NullableDirectiveTrivia(
                    SyntaxFactory.Token(SyntaxKind.DisableKeyword),
                    true
                );

                var outputClassDeclaration = ClassDeclaration(classSymbol.Name)
                    .AddModifiers(classDeclaration.Modifiers.ToArray())
                    .AddMembers(members.ToArray())
                    .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Trivia(nullableEnable)))
                    .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Trivia(nullableDisable)));

                var outputNamespaceDeclaration = NamespaceDeclaration(GetFullNamespace(classSymbol))
                    .AddMembers(outputClassDeclaration);

                // Create a compilation unit (the top-level structure of a C# file)
                var outputCompilationUnit = CompilationUnit()
                    .AddUsings(UsingDirective(IdentifierName("System")))
                    .AddUsings(UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection")))
                    .AddMembers(outputNamespaceDeclaration)
                    .NormalizeWhitespace(); 
                
                // Add generated source to the compilation
                string output = outputCompilationUnit.NormalizeWhitespace().ToFullString();
                _context.AddSource($"{classSymbol.Name}.g.cs", SourceText.From(output, Encoding.UTF8));
            }
        }
        
        private StatementSyntax? CreateStatement(IParameterSymbol serviceCollectionParameter, DependencyService service)
        {
            var arguments = GetArgumentsForService(service);
            var types = GetArityTypesForService(service);
            var functionName = GetFunctionName(service);
            

            IdentifierNameSyntax serviceCollectionSymbol = SyntaxFactory.IdentifierName(serviceCollectionParameter.Name); ;
            if(serviceCollectionParameter.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
            {
                serviceCollectionSymbol = SyntaxFactory.IdentifierName(serviceCollectionParameter.Name)
                    .WithAdditionalAnnotations(SyntaxAnnotation.ElasticAnnotation);
            }
            

            var functionToCall = SyntaxFactory.GenericName(functionName)
                .WithTypeArgumentList(
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList<TypeSyntax>(types)
                        )
                    );
            
            
            if (serviceCollectionParameter.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
            {
                return SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.ConditionalAccessExpression(
                        serviceCollectionSymbol,
                        SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberBindingExpression(SyntaxFactory.Token(SyntaxKind.DotToken), functionToCall)
                            )
                            .WithArgumentList(
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.SeparatedList(arguments)
                                )
                            )
                    )
                );
            }
            else
            {
                return SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    serviceCollectionSymbol,
                                    functionToCall
                                )
                            )
                            .WithArgumentList(
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.SeparatedList(arguments)
                                )
                            )
                    );
            }

        }

        
        
        /// <summary>
        /// Gets the name of the function to register the service
        /// </summary>
        /// <param name="service">The service to get registered</param>
        /// <returns>A identifier syntax of the function call</returns>
        private SyntaxToken GetFunctionName(DependencyService service)
        {
            string functionString = "Add";
            if(service.KeyedServiceName is not null)
            {
                functionString += "Keyed";
            }
            switch(service.ServiceLifetime)
            {
                case 1:
                    functionString += "Scoped";
                    break;
                case 2:
                    functionString += "Transient";
                    break;
                default:
                    functionString += "Singleton";
                    break;
            }

            return SyntaxFactory.Identifier(functionString);
        }

        /// <summary>
        /// Gets the arguments of the function call for the given service
        /// </summary>
        /// <param name="service">The service to register</param>
        /// <returns>The arguments list for the function call</returns>
        private ArgumentSyntax[] GetArgumentsForService(DependencyService service)
        {
            if (string.IsNullOrEmpty(service.KeyedServiceName))
            {
                return Array.Empty<ArgumentSyntax>();
            }

            return new[]
            {
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(service.KeyedServiceName!)))
            };
        }

        private IdentifierNameSyntax[] GetArityTypesForService(DependencyService service)
        {
            if(service.FullyQualifiedInterfaceName is not null)
            {
                return new[]
                {
                    SyntaxFactory.IdentifierName(service.FullyQualifiedInterfaceName),
                    SyntaxFactory.IdentifierName(service.FullyQualifiedName)
                };
            }
            
            return new[]
            {
                SyntaxFactory.IdentifierName(service.FullyQualifiedName)
            };
            
        }
        
        private IdentifierNameSyntax GetFullNamespace(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                throw new ArgumentNullException(nameof(typeSymbol));
            }

            // Start with the containing namespace
            var namespaceSymbol = typeSymbol.ContainingNamespace;

            // If the type is in the global namespace, just return an empty string
            if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace)
            {
                return SyntaxFactory.IdentifierName(typeSymbol.Name);
            }

            // Use a StringBuilder to build the full namespace string
            var namespaceBuilder = new System.Text.StringBuilder(namespaceSymbol.Name);

            // Traverse parent namespaces
            while ((namespaceSymbol = namespaceSymbol.ContainingNamespace) != null && !namespaceSymbol.IsGlobalNamespace)
            {
                namespaceBuilder.Insert(0, namespaceSymbol.Name + ".");
            }

            return SyntaxFactory.IdentifierName(namespaceBuilder.ToString());
        }
    }
}