using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.DotnetRuntime.Extensions;

namespace GaoWare.DependencyInjection.Generator;

public partial class DependencyInjectionGenerator
{
    private class DependenciesParser
    {
        public static readonly string RegisterServiceAttribute =
            "GaoWare.DependencyInjection.Attributes.RegisterServiceAttribute";

        public static readonly string RegisterInterfaceAttribute =
            "GaoWare.DependencyInjection.Attributes.RegisterInterfaceAttribute";


        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;
        private readonly Action<Diagnostic> _reportDiagnostic;

        public DependenciesParser(Compilation compilation, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
        {
            _compilation = compilation;
            _cancellationToken = cancellationToken;
            _reportDiagnostic = reportDiagnostic;
        }

        public IReadOnlyList<DependencyService> ParseDependencies(ImmutableArray<ClassDeclarationSyntax?> services)
        {
            INamedTypeSymbol? registerServiceAttribute =
                _compilation.GetBestTypeByMetadataName(RegisterServiceAttribute);
            if (registerServiceAttribute == null)
            {
                // nothing to do if this type isn't available
                return Array.Empty<DependencyService>();
            }

            INamedTypeSymbol? registerInterfaceService =
                _compilation.GetBestTypeByMetadataName(RegisterInterfaceAttribute);
            if (registerInterfaceService == null)
            {
                // nothing to do if this type isn't available
                return Array.Empty<DependencyService>();
            }
            
            INamedTypeSymbol? serviceLifetype =
                _compilation.GetBestTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime");
            if (serviceLifetype == null)
            {
                // nothing to do if this type isn't available
                return Array.Empty<DependencyService>();
            }
            
            INamedTypeSymbol stringSymbol = _compilation.GetSpecialType(SpecialType.System_String);


            List<DependencyService> values = new List<DependencyService>();
            foreach (IGrouping<SyntaxTree?, ClassDeclarationSyntax?> group in services.GroupBy(x => x?.SyntaxTree))
            {
                if (group?.Key is null)
                {
                    continue;
                }

                SyntaxTree syntaxTree = group.Key;
                SemanticModel sm = _compilation.GetSemanticModel(syntaxTree);

                foreach (var classDec in group)
                {
                    if (classDec is null)
                    {
                        continue;
                    }

                    // stop if we're asked to
                    _cancellationToken.ThrowIfCancellationRequested();
                    DependencyService? service = null;
                    
                    INamedTypeSymbol? serviceClassSymbol = sm.GetDeclaredSymbol(classDec, _cancellationToken) as INamedTypeSymbol;
                    if (serviceClassSymbol is null)
                    {
                        continue;
                    }
                    Debug.Assert(serviceClassSymbol != null, "service class is present.");
                    
                    foreach (var cal in classDec.AttributeLists)
                    {
                        foreach (var ca in cal.Attributes)
                        {
                            IMethodSymbol? attrCtorSymbol =
                                sm.GetSymbolInfo(ca, _cancellationToken).Symbol as IMethodSymbol;
                            if (attrCtorSymbol == null ||
                                !registerServiceAttribute.Equals(attrCtorSymbol.ContainingType,
                                    SymbolEqualityComparer.Default))
                            {
                                // badly formed attribute definition, or not the right attribute
                                continue;
                            }
                            
                            ImmutableArray<AttributeData> boundAttributes = serviceClassSymbol!.GetAttributes();
                            
                            if (boundAttributes.Length == 0)
                            {
                                continue;
                            }

                            foreach (AttributeData attributeData in boundAttributes)
                            {
                                if (!SymbolEqualityComparer.Default.Equals(attributeData.AttributeClass,
                                        registerServiceAttribute))
                                {
                                    continue;
                                }

                                string fullyQualifiedName =
                                    serviceClassSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                                service = this.ParseAttributeData(fullyQualifiedName, attributeData);
                            }
                            

                            if (service is not null &&
                                service.Interface is not null &&
                                this.CheckClassInheritInterface(serviceClassSymbol, service.Interface))
                            {
                                service = null;
                            }

                            if (service is null ||
                                service.Interface is not null)
                            {
                                continue;
                            }
                            
                            // Go through the interfaces to find a valid one, if not already set
                            foreach (var interf in serviceClassSymbol.AllInterfaces)
                            {
                                service.Interface = this.CheckInterfaceHasAttribute(interf, registerInterfaceService);
                                if (!string.IsNullOrEmpty(service.FullyQualifiedInterfaceName))
                                {
                                    break;
                                }
                            }
                        }
                        
                    }

                    if (service is not null)
                    {
                        values.Add(service);
                    }
                }
            }

            // 
            return values;
        }

        private DependencyService? ParseAttributeData(string fullyQualifiedName, AttributeData attribute)
        {
            DependencyService ret = new();
            ret.FullyQualifiedName = fullyQualifiedName;
            int? lifetimeParameter = null;
            bool hasMisconfiguredInput = false;
            // Parse constructor arguments
            if (attribute.ConstructorArguments.Any())
            {
                switch (attribute.ConstructorArguments.Length)
                {
                    case 1:
                        // ServiceLifetime only parameter
                        lifetimeParameter = attribute.ConstructorArguments[0].Value as int?;
                        break;
                }
            }
            
            // Named Members
             // argument syntax takes parameters. e.g. EventId = 0
            // supports: e.g. [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "custom message")]
            if (attribute.NamedArguments.Any())
            {
                foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
                {
                    TypedConstant typedConstant = namedArgument.Value;
                    if (typedConstant.Kind == TypedConstantKind.Error)
                    {
                        hasMisconfiguredInput = true;
                        break; // if a compilation error was found, no need to keep evaluating other args
                    }

                    TypedConstant value = namedArgument.Value;
                    switch (namedArgument.Key)
                    {
                        case "ServiceLifetime":
                            if (lifetimeParameter is null)
                            {
                                lifetimeParameter = value.Value as int?;
                                if (lifetimeParameter is null)
                                {
                                    hasMisconfiguredInput = true;
                                }
                            }
                            break;
                        case "InterfaceType":
                            if (value.Value is INamedTypeSymbol typeArgument)
                            {
                                ret.Interface = typeArgument;
                            }
                            break;
                        case "ServiceKey":
                            ret.KeyedServiceName = value.Value as string;
                            
                            break;
                    }
                }
            }
            if(lifetimeParameter is not null &&
               hasMisconfiguredInput == false)
            {
                ret.ServiceLifetime = lifetimeParameter.Value;
                return ret;
            }
            

            return null;
        }

        private bool CheckClassInheritInterface(INamedTypeSymbol interfaceToCheck, ITypeSymbol interfaceSymbol)
        {
            if(SymbolEqualityComparer.Default.Equals(interfaceSymbol, interfaceToCheck))
            {
                return true;
            }
            
            // Go through all interfaces and check if one has an attribute
            foreach (var interf in interfaceSymbol.AllInterfaces)
            {
                if (this.CheckClassInheritInterface(interf, interfaceSymbol))
                {
                    return true;
                }
            }
            return false;
        }
        
        private INamedTypeSymbol? CheckInterfaceHasAttribute(INamedTypeSymbol interfaceSymbol, INamedTypeSymbol compareSymbol)
        {
            // Extract the attribute
            if(interfaceSymbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, compareSymbol)))
            {
                return interfaceSymbol;
            }

            INamedTypeSymbol? value = null;
            // Go through all interfaces and check if one has an attribute
            foreach (var interf in interfaceSymbol.AllInterfaces)
            {
                value = this.CheckInterfaceHasAttribute(interf, compareSymbol);
                if (value is not null)
                {
                    return value;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Represents the data for a service to register
    /// </summary>
    private class DependencyService
    {
        /// <summary>
        /// Gets or sets the fully qualified name of the service
        /// </summary>
        public string FullyQualifiedName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the interface of this service
        /// </summary>
        public INamedTypeSymbol? Interface { get; set; }

        /// <summary>
        /// Gets the fully qualified name of the interface if set 
        /// </summary>
        public string? FullyQualifiedInterfaceName =>
            Interface?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        /// <summary>
        /// Gets or set the keyed name for the service
        /// </summary>
        public string? KeyedServiceName { get; set; }
        /// <summary>
        /// Gets or set the service lifetime for this service
        /// </summary>
        public int ServiceLifetime { get; set; } = 1;
    }
    
}