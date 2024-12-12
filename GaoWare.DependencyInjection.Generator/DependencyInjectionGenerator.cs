using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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
public sealed partial class DependencyInjectionGenerator : IIncrementalGenerator
{

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CodeGenerator.DependencyRegistrationAttribute,
                (node, _) => node is MethodDeclarationSyntax,
                (ctx, _) => ctx.TargetNode.Parent as ClassDeclarationSyntax)
            .Where(static m => m is not null);
        
        var serviceDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DependenciesParser.RegisterServiceAttribute,
                (node, _) => node is ClassDeclarationSyntax,
                (ctx, _) => ctx.TargetNode as ClassDeclarationSyntax)
            .Where(static m => m is not null);
        
        // Combine classDeclarations and serviceDeclarations with the Compilation
        IncrementalValueProvider<(Compilation, ImmutableArray<ClassDeclarationSyntax?>, ImmutableArray<ClassDeclarationSyntax?>)> compilationClassesAndServices =
            context.CompilationProvider
                .Combine(classDeclarations.Collect())
                .Combine(serviceDeclarations.Collect())
                .Select((combined, _) =>
                {
                    var (compilation, classDecls) = combined.Left;
                    var serviceDecls = combined.Right;
                    return (compilation, classDecls, serviceDecls);
                });
        
        context.RegisterSourceOutput(compilationClassesAndServices, static (spc, combinedValues) => Execute(combinedValues.Item1, combinedValues.Item2, combinedValues.Item3, spc));
    }

    private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax?> classes, ImmutableArray<ClassDeclarationSyntax?> services, SourceProductionContext context)
    {
        var parser = new DependenciesParser(compilation, context.ReportDiagnostic, context.CancellationToken);

        var dependencies = parser.ParseDependencies(services);

        var generator = new CodeGenerator(context, compilation, context.CancellationToken);
        generator.GenerateOutput(dependencies, classes);
    }
}