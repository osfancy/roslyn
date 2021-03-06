﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.GenerateFromMembers
{
    internal abstract partial class AbstractGenerateFromMembersCodeRefactoringProvider : CodeRefactoringProvider
    {
        protected AbstractGenerateFromMembersCodeRefactoringProvider()
        {
        }

        protected async Task<SelectedMemberInfo> GetSelectedMemberInfoAsync(
            Document document, TextSpan textSpan, CancellationToken cancellationToken)
        {
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            var semanticFacts = document.GetLanguageService<ISemanticFactsService>();

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var selectedDeclarations = syntaxFacts.GetSelectedMembers(root, textSpan);

            if (selectedDeclarations.Length > 0)
            {
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var selectedMembers = selectedDeclarations.SelectMany(
                    d => semanticFacts.GetDeclaredSymbols(semanticModel, d, cancellationToken)).WhereNotNull().ToImmutableArray();
                if (selectedMembers.Length > 0)
                {
                    var containingType = selectedMembers.First().ContainingType;
                    if (containingType != null)
                    {
                        return new SelectedMemberInfo(containingType, selectedDeclarations, selectedMembers);
                    }
                }
            }

            return null;
        }

        // Can use non const fields and properties with setters in them.
        protected static bool IsWritableInstanceFieldOrProperty(ISymbol symbol)
            => IsInstanceFieldOrProperty(symbol) &&
               IsWritableFieldOrProperty(symbol);

        private static bool IsWritableFieldOrProperty(ISymbol symbol)
        {
            switch (symbol)
            {
                case IFieldSymbol field: return !field.IsConst && field.AssociatedSymbol == null;
                case IPropertySymbol property: return property.IsWritableInConstructor();
                default: return false;
            }
        }

        protected static bool IsInstanceFieldOrProperty(ISymbol symbol)
            => !symbol.IsStatic && (IsField(symbol) || IsProperty(symbol));

        private static bool IsProperty(ISymbol symbol)
            => symbol.Kind == SymbolKind.Property;

        private static bool IsField(ISymbol symbol)
            => symbol.Kind == SymbolKind.Field;

        protected ImmutableArray<IParameterSymbol> DetermineParameters(
            ImmutableArray<ISymbol> selectedMembers)
        {
            var parameters = ArrayBuilder<IParameterSymbol>.GetInstance();

            foreach (var symbol in selectedMembers)
            {
                var type = symbol is IFieldSymbol
                    ? ((IFieldSymbol)symbol).Type
                    : ((IPropertySymbol)symbol).Type;

                parameters.Add(CodeGenerationSymbolFactory.CreateParameterSymbol(
                    attributes: null,
                    refKind: RefKind.None,
                    isParams: false,
                    type: type,
                    name: symbol.Name.ToCamelCase().TrimStart(s_underscore)));
            }

            return parameters.ToImmutableAndFree();
        }

        private static readonly char[] s_underscore = { '_' };

        protected IMethodSymbol GetDelegatedConstructor(
            INamedTypeSymbol containingType,
            ImmutableArray<IParameterSymbol> parameters)
        {
            var q =
                from c in containingType.InstanceConstructors
                orderby c.Parameters.Length descending
                where c.Parameters.Length > 0 && c.Parameters.Length < parameters.Length
                where c.Parameters.All(p => p.RefKind == RefKind.None) && !c.Parameters.Any(p => p.IsParams)
                let constructorTypes = c.Parameters.Select(p => p.Type)
                let symbolTypes = parameters.Take(c.Parameters.Length).Select(p => p.Type)
                where constructorTypes.SequenceEqual(symbolTypes)
                select c;

            return q.FirstOrDefault();
        }

        protected IMethodSymbol GetMatchingConstructor(INamedTypeSymbol containingType, ImmutableArray<IParameterSymbol> parameters)
            => containingType.InstanceConstructors.FirstOrDefault(c => MatchesConstructor(c, parameters));

        private bool MatchesConstructor(IMethodSymbol constructor, ImmutableArray<IParameterSymbol> parameters)
            => parameters.Select(p => p.Type).SequenceEqual(constructor.Parameters.Select(p => p.Type));

        protected static readonly SymbolDisplayFormat SimpleFormat =
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                parameterOptions: SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
    }
}