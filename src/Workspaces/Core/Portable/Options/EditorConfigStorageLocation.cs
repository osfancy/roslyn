﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics.Analyzers.NamingStyles;
using static Microsoft.CodeAnalysis.CodeStyle.CodeStyleHelpers;

namespace Microsoft.CodeAnalysis.Options
{
    /// <summary>
    /// Specifies that an option should be read from an .editorconfig file.
    /// </summary>
    internal sealed class EditorConfigStorageLocation : OptionStorageLocation
    {
        public string KeyName { get; }

        private Func<string, Type, object> _parseValue;

        private static Func<string, Type, object> _cachedParseValue = (s, type) =>
        {
            if (type == typeof(int))
            {
                var value = 0;
                return int.TryParse(s, out value) ? (object)value : null;
            }
            else if (type == typeof(bool))
            {
                var value = false;
                return bool.TryParse(s, out value) ? (object)value : null;
            }
            else if (type == typeof(CodeStyleOption<bool>))
            {
                var value = CodeStyleOption<bool>.Default;
                return TryParseEditorConfigCodeStyleOption(s, out value) ? value : null;
            }
            else
            {
                throw new NotSupportedException(WorkspacesResources.Option_0_has_an_unsupported_type_to_use_with_1_You_should_specify_a_parsing_function);
            }
        };

        private Func<IReadOnlyDictionary<string, object>, Type, (object result, bool succeeded)> _tryParseDictionary;

        private static Func<IReadOnlyDictionary<string, object>, Type, (object result, bool succeeded)> _cachedTryParseDictionary = (dictionary, type) =>
        {
            if (type == typeof(NamingStylePreferences))
            {
                var result = EditorConfigNamingStyleParser.GetNamingStylesFromDictionary(dictionary);
                if (!result.NamingRules.Any() &&
                    !result.NamingStyles.Any() &&
                    !result.SymbolSpecifications.Any())
                {
                    return (result: result, succeeded: false);
                }

                return (result: result, succeeded: true);
            }
            else
            {
                throw new NotSupportedException(WorkspacesResources.Option_0_has_an_unsupported_type_to_use_with_1_You_should_specify_a_parsing_function);
            }
        };

        public bool TryParseReadonlyDictionary(IReadOnlyDictionary<string, object> allRawConventions, Type type, out object result)
        {
            if (_parseValue != null && KeyName != null)
            {
                if (allRawConventions.TryGetValue(KeyName, out object value))
                {
                    result = _parseValue(value.ToString(), type);
                    return result != null;
                }
            }
            else if (_tryParseDictionary != null)
            {
                var tuple = _tryParseDictionary(allRawConventions, type);
                result = tuple.result;
                return tuple.succeeded;
            }

            result = null;
            return false;
        }

        public EditorConfigStorageLocation(string keyName)
        {
            KeyName = keyName;
            _parseValue = _cachedParseValue;
        }

        public EditorConfigStorageLocation(string keyName, Func<string, object> parseValue)
        {
            KeyName = keyName;

            // If we're explicitly given a parsing function we can throw away the type when parsing
            _parseValue = (s, type) => parseValue(s);
        }

        public EditorConfigStorageLocation()
        {
            // If the user didn't pass a keyName assume we need to parse the entire dictionary
            _tryParseDictionary = _cachedTryParseDictionary;
        }

        public EditorConfigStorageLocation(Func<IReadOnlyDictionary<string, object>, (object result, bool succeeded)> tryParseDictionary)
        {
            // If we're explicitly given a parsing function we can throw away the type when parsing
            _tryParseDictionary = (dictionary, type) => tryParseDictionary(dictionary);
        }
    }
}
