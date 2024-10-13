using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.Text;

namespace GaoWare.DependencyInjection.Generator;

[Generator]
public class DependencyInjectionGenerator : IIncrementalGenerator
{

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(this.GenerateAttributes);
    // Collect all classes with RegisterServiceAttribute and interfaces with RegisterInterfaceAttribute
        var serviceClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: HasClassAttribute,
                transform: GetClassSymbol)
            .Where(symbol => symbol != null)
            .Collect();

        // Look for methods with RegisterAllServicesAttribute
        var registrationMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: HasMethodAttribute,
                transform: GetMethodSymbol)
            .Where(symbol => symbol.Item1 != null && symbol.Item2 != null)
            .Collect();

        var combined = registrationMethods.Combine(serviceClasses);
    
        // Generate output when a method with RegisterAllServices is found
        context.RegisterSourceOutput(combined, (ctx, combi) =>
        {
            var methods = combi.Left;
            //var services = combi.Right;

            // Lookup all services

            IEnumerable<DependencyService> services = LookupServices(combi.Right);

            // 
             // Generate output based on the method's name
            foreach (var method in methods)
            {
                GenerateRegistrationCode(ctx, method, services);
             }
        });
    }

    private static bool HasClassAttribute(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
            classDeclaration.AttributeLists.Count > 0;
    }
    private static bool HasMethodAttribute(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is MethodDeclarationSyntax methodDeclaration && 
            methodDeclaration.AttributeLists.Count > 0;
    }

    private static INamedTypeSymbol? GetClassSymbol(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var symbol = ModelExtensions.GetDeclaredSymbol(semanticModel, classDeclaration);

        // Check if the class has the RegisterService attribute
        if (symbol != null && symbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "RegisterServiceAttribute"))
        {
            return symbol as INamedTypeSymbol;
        }

        return null;
    }

    private static (IMethodSymbol?, MethodDeclarationSyntax?) GetMethodSymbol(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var symbol = ModelExtensions.GetDeclaredSymbol(semanticModel, methodDeclaration);

        // Check if the method has the RegisterAllServices attribute
        if (symbol != null && symbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "GenerateServiceRegistrationAttribute"))
        {
            return (symbol as IMethodSymbol, methodDeclaration);
        }

        return (null,null);
    }

    private IEnumerable<DependencyService> LookupServices(ImmutableArray<INamedTypeSymbol?> services)
    {
        List<DependencyService> values = new List<DependencyService>();
        foreach(var service in services)
        {
            if(service == null)
            {
                continue;
            }
            // Extract the attribute
            AttributeData? attr = service.GetAttributes().SingleOrDefault(a => a.AttributeClass?.Name == "RegisterServiceAttribute");
            
            if (attr != null)
            {
                var ds = new DependencyService();
                ds.FullyQualifiedName = service.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
 
                if(attr.ConstructorArguments.Length == 3)
                {
                    // lifetime, serviceName, interfaceType
                    //attr.Con
                    //ds.ServiceLifetime = //GetValueAsType<ServiceLifetime>(attr.ConstructorArguments[0].Value, ServiceLifetime.Scoped);
                    // Check if type
                    int? lifetime = attr.ConstructorArguments[0].Value as int?;

                    if (lifetime != null)
                    {
                        ds.ServiceLifetime = lifetime.Value;
                    }
                    



                    ds.KeyedServiceName = attr.ConstructorArguments[1].Value as string;
 
                    var typeArgument = attr.ConstructorArguments[2].Value as ITypeSymbol;
                    if(typeArgument != null)
                    {
                        ds.FullyQualifiedInterfaceName = typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
 
                    if(string.IsNullOrEmpty(ds.FullyQualifiedInterfaceName))
                    {
                        // Go through all interfaces and check if one has an attribute
                        foreach (var interf in service.AllInterfaces)
                        {
                            ds.FullyQualifiedInterfaceName = this.CheckInterfaceHasAttribute(interf);
                            if(!string.IsNullOrEmpty(ds.FullyQualifiedInterfaceName))
                            {
                                break;
                            }
                        }
                    }
                    values.Add(ds);
                }
            }
        }

        return values;
    }

    public string? CheckInterfaceHasAttribute(INamedTypeSymbol interfaceSymbol)
    {
        // Extract the attribute
        if(interfaceSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "RegisterInterfaceAttribute"))
        {
            return interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        string? value = null;
        // Go through all interfaces and check if one has an attribute
        foreach (var interf in interfaceSymbol.AllInterfaces)
        {
            value = this.CheckInterfaceHasAttribute(interf);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private void GenerateRegistrationCode(SourceProductionContext context, (IMethodSymbol?, MethodDeclarationSyntax?) method, IEnumerable<DependencyService> services )
    {
        if (method.Item1 is null ||method.Item2 is null) return;

        var namespaceName = method.Item1.ContainingNamespace.ToDisplayString();
        var className = method.Item1.ContainingType.Name;

        List<StatementSyntax> statements = new List<StatementSyntax>();
        // Check if there is a parameter of type IServiceCollection
        var serviceCollectionParameter = method.Item1.Parameters.FirstOrDefault(p =>
            p.Type.ToDisplayString().StartsWith("Microsoft.Extensions.DependencyInjection.IServiceCollection"));


        if (serviceCollectionParameter != null)
        {
            // Generate lines for classes that have RegisterService attribute
            foreach (var serviceClass in services)
            {
                statements.Add(DependencyInjectionGenerator.CreateAddStatementWithInterface(serviceCollectionParameter, serviceClass));
            }
        }

        if(serviceCollectionParameter is not null &&
            method.Item1.ReturnType.ToDisplayString().Equals(serviceCollectionParameter.Type.ToDisplayString()))
        {
            statements.Add(
                SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(serviceCollectionParameter.Name)
                )
            );

        }
        else if(!method.Item1.ReturnsVoid)
        {
            statements.Add(
                SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(
                    SyntaxFactory.ParseTypeName(method.Item1.ReturnType.ToDisplayString()))
                )
            );
        } 

        // Create method body
        MethodDeclarationSyntax methodDeclaration =
            MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(method.Item1.Name))
            .AddModifiers(method.Item2.Modifiers.ToArray())
            .AddParameterListParameters(method.Item2.ParameterList.Parameters.ToArray())
            .WithReturnType(method.Item2.ReturnType)
            .AddBodyStatements(statements.ToArray());

        var classSymbol = method.Item1.ContainingType as ITypeSymbol;
        if (classSymbol is null)
        {
            return;
        }

        ClassDeclarationSyntax classDeclaration;

        if (serviceCollectionParameter is not null && serviceCollectionParameter.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
        {
            // Create the nullable enable directive
            var nullableEnable = SyntaxFactory.NullableDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.EnableKeyword),
                true
            );
            // Create the nullable enable directive
            var nullableDisable = SyntaxFactory.NullableDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.DisableKeyword),
                true
            );

            classDeclaration = ClassDeclaration(classSymbol.Name)
                .AddModifiers(GetModifiers(classSymbol))
                .AddMembers(methodDeclaration)
                .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Trivia(nullableEnable)))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Trivia(nullableDisable)));
        }
        else
        {
            classDeclaration = ClassDeclaration(classSymbol.Name)
                .AddModifiers(GetModifiers(classSymbol))
                .AddMembers(methodDeclaration);
        }



        var namespaceDeclaration = NamespaceDeclaration(IdentifierName(GetFullNamespace(classSymbol)))
            .AddMembers(classDeclaration);

        // Create a compilation unit (the top-level structure of a C# file)
        var compilationUnit = CompilationUnit()
            .AddUsings(UsingDirective(IdentifierName("System")))
            .AddUsings(UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection")))          
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace(); // This makes the code well-formatted with proper indentation


        // string t = builder.ToString();
        //Debugger.Launch();
        // Add generated source to the compilation
        string output = compilationUnit.NormalizeWhitespace().ToFullString();
        context.AddSource($"{className}.{method.Item1.Name}.g.cs", SourceText.From(output, Encoding.UTF8));
    }

    public static string GetFullNamespace(ITypeSymbol typeSymbol)
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
            return string.Empty;
        }

        // Use a StringBuilder to build the full namespace string
        var namespaceBuilder = new System.Text.StringBuilder(namespaceSymbol.Name);

        // Traverse parent namespaces
        while ((namespaceSymbol = namespaceSymbol.ContainingNamespace) != null && !namespaceSymbol.IsGlobalNamespace)
        {
            namespaceBuilder.Insert(0, namespaceSymbol.Name + ".");
        }

        return namespaceBuilder.ToString();
    }

    private static StatementSyntax CreateAddStatementWithInterface(IParameterSymbol serviceCollectionName, DependencyService service)
    {
        string functionString = "Add";
        ArgumentSyntax[] arguments = Array.Empty<ArgumentSyntax>();
        List<TypeSyntax> types = new();
        if(service.KeyedServiceName is not null)
        {
            arguments = new[] { SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(service.KeyedServiceName))) };
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

        if(service.FullyQualifiedInterfaceName is not null)
        {
            types.Add(SyntaxFactory.IdentifierName(service.FullyQualifiedInterfaceName));
        }
        types.Add(SyntaxFactory.IdentifierName(service.FullyQualifiedName));

        IdentifierNameSyntax serviceCollectionSymbol = SyntaxFactory.IdentifierName(serviceCollectionName.Name); ;
        if(serviceCollectionName.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
        {
            serviceCollectionSymbol = SyntaxFactory.IdentifierName(serviceCollectionName.Name)
                .WithAdditionalAnnotations(SyntaxAnnotation.ElasticAnnotation);
        }

        var functionToCall = SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier(functionString))
                    .WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SeparatedList<TypeSyntax>(types.ToArray())
                        )
                    );

        if (serviceCollectionName.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
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

    private static SyntaxToken[] GetModifiers(ITypeSymbol typeSymbol)
    {
        var modifiers = new List<SyntaxToken>();

        // Add accessibility modifiers
        switch (typeSymbol.DeclaredAccessibility)
        {
            case Accessibility.Public:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                break;
            case Accessibility.Private:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.PrivateKeyword));
                break;
            case Accessibility.Internal:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.InternalKeyword));
                break;
            case Accessibility.Protected:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.ProtectedKeyword));
                break;
            case Accessibility.ProtectedOrInternal:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.ProtectedKeyword));
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.InternalKeyword));
                break;
        }

        // Add other method modifiers
        if (typeSymbol.IsStatic)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        if (typeSymbol.IsAbstract)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.AbstractKeyword));
        if (typeSymbol.IsVirtual)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.VirtualKeyword));
        if (typeSymbol.IsOverride)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.OverrideKeyword));
        if (typeSymbol.IsSealed)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.SealedKeyword));

        modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

        return modifiers.ToArray();
    }
    // Creates a parameter list from the method's parameters
    private static ParameterSyntax[] CreateParameterList(IMethodSymbol methodSymbol)
    {
        var parameters = new List<ParameterSyntax>();

        foreach (var parameter in methodSymbol.Parameters)
        {
            // Extract the parameter type
            var parameterType = SyntaxFactory.ParseTypeName(parameter.Type.ToDisplayString());

            // Create the parameter syntax
            var parameterSyntax = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                .WithType(parameterType);

            // Add additional information if needed (like ref, out, params, etc.)
            if (parameter.RefKind == RefKind.Ref)
            {
                parameterSyntax = parameterSyntax.WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword)));
            }
            else if (parameter.RefKind == RefKind.Out)
            {
                parameterSyntax = parameterSyntax.WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword)));
            }
            else if (parameter.IsParams)
            {
                parameterSyntax = parameterSyntax.WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)));
            }

            // Add the parameter to the list
            parameters.Add(parameterSyntax);
        }

        // Create a parameter list from the individual parameters
        return parameters.ToArray();
    }

    private class DependencyService
    {
        public string FullyQualifiedName { get; set; } = string.Empty;
        public string? FullyQualifiedInterfaceName { get; set; }
        public string? KeyedServiceName { get; set; }
        public int ServiceLifetime { get; set; } = 1;
    }

    private void GenerateAttributes(IncrementalGeneratorPostInitializationContext ctx)
    {
        ctx.AddSource("DependencyInjection.g.cs", SourceText.From(GaoWareStringHelpers.AttributeStrings, Encoding.UTF8));
    }
}