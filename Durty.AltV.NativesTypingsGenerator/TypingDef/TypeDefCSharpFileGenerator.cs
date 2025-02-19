﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AltV.NativesDb.Reader.Extensions;
using AltV.NativesDb.Reader.Models.NativeDb;
using Durty.AltV.NativesTypingsGenerator.Converters;
using Durty.AltV.NativesTypingsGenerator.Models.Typing;

namespace Durty.AltV.NativesTypingsGenerator.TypingDef
{
    public class TypeDefCSharpFileGenerator
    {
        private readonly TypeDef _typeDefFile;
        private readonly bool _generateDocumentation;

        public TypeDefCSharpFileGenerator(
            TypeDef typeDefFile,
            bool generateDocumentation = true)
        {
            _typeDefFile = typeDefFile;
            _generateDocumentation = generateDocumentation;
        }

        public string Generate(bool generateHeader = true, List<string> customHeaderLines = null)
        {
            StringBuilder fileContent = new StringBuilder(string.Empty);
            if (generateHeader)
            {
                if (customHeaderLines != null)
                {
                    foreach (var customHeaderLine in customHeaderLines)
                    {
                        fileContent.Append($"//{customHeaderLine}\n");
                    }
                }
                fileContent.Append("\n");
            }

            foreach (TypeDefModule typeDefModule in _typeDefFile.Modules)
            {
                fileContent.Append(GenerateModule(typeDefModule));
                fileContent.Append("\n");
            }

            return fileContent.ToString();
        }

        private StringBuilder GenerateModule(TypeDefModule typeDefModule)
        {
            StringBuilder result = new StringBuilder(string.Empty);
            result.Append("// THIS IS AN AUTOGENERATED FILE. DO NOT EDIT THIS FILE DIRECTLY.\n\n");
            result.Append($"using System.Numerics;\n");
            result.Append($"using System.Reflection;\n");
            result.Append($"using AltV.Net.Shared.Utils;\n");
            result.Append($"using AltV.Net.Client.Elements.Interfaces;\n");
            result.Append($"using System.Runtime.InteropServices;\n\n");
            result.Append($"namespace AltV.Net.Client\n{{\n");
            result.Append($"\tpublic unsafe interface INatives\n\t{{\n");
            result = typeDefModule.Functions.Aggregate(result, (current, typeDefFunction) => current.Append($"{GenerateFunctionDefinition(typeDefFunction, "\t\t")};\n"));
            result.Append("\t}\n\n");
            result.Append($"\tpublic unsafe class Natives : INatives\n\t{{\n");
            result.Append($"\t\tprivate Dictionary<ulong, IntPtr> funcTable;\n");
            result.Append($"\t\tprivate delegate* unmanaged[Cdecl]<nint, void> freeString;\n");
            foreach (var typeDefFunction in typeDefModule.Functions)
            {
                result.Append($"\t\tprivate {GetUnmanagedDelegateType(typeDefFunction)} {GetFixedTypeDefFunctionName(typeDefFunction.Name)};\n");
            }
            result.Append($"\n");
            result.Append($"\t\tpublic Natives(ILibrary library)\n\t\t{{\n");
            result.Append($"\t\t\tfreeString = library.Shared.FreeString;\n");
            result.Append($"\t\t\tfuncTable = Marshal.PtrToStructure<FunctionTable>(library.Client.GetNativeFuncTable()).GetTable();\n");
            result.Append($"\t\t}}\n\n");

            result = typeDefModule.Functions.Aggregate(result, (current, typeDefFunction) => current.Append($"{GenerateFunction(typeDefFunction)}\n"));
            result.Append("\t}\n");
            result.Append("}");
            return result;
        }

        private string GetFixedTypeDefFunctionName(string name)
        {
            return "fn_" + (name.StartsWith("_") ? name : "_" + name);
        }

        private string GetUnmanagedDelegateType(TypeDefFunction function)
        {
            var converter = new NativeTypeToCSharpTypingConverter();
            return $"delegate* unmanaged[Cdecl]<bool*, {string.Join("", function.Parameters.Select(p => $"{converter.Convert(null, p.NativeType, p.IsReference, true)}, "))}{converter.Convert(null, function.ReturnType.NativeType[0], false, true)}>";
        }

        private string GetFixedTypeDefParameterName(string name)
        {
            return IsParameterNameReservedCSharpKeyWord(name) ? "@" + name : name;
        }
        private string GetEscapedTypeDefParameterName(string name)
        {
            return "_" + name;
        }

        private StringBuilder GenerateFunctionDefinition(TypeDefFunction typeDefFunction, string prepend = "", bool forceIgnoreDocs = false, bool isInterface = true)
        {
            StringBuilder result = new StringBuilder(string.Empty);
            var overloads = GetOverloads(typeDefFunction);
            
            foreach (var (overloadDef, overloadImpl) in overloads)
            {
                if (_generateDocumentation && !forceIgnoreDocs)
                {
                    result.Append(GenerateFunctionDocumentation(typeDefFunction));
                }

                if (isInterface)
                {
                 
                    result.Append(prepend + overloadDef + ";\n");   
                }
                else
                {
                    result.Append(prepend + overloadDef + " => " + overloadImpl + "\n");
                }
            }
            
            if (_generateDocumentation && !forceIgnoreDocs)
            {
                result.Append(GenerateFunctionDocumentation(typeDefFunction));
            }

            var cSharpReturnType = new NativeTypeToCSharpTypingConverter().Convert(null, typeDefFunction.ReturnType.NativeType[0], false);
            result.Append($"{prepend}{cSharpReturnType} {typeDefFunction.Name.FirstCharToUpper()}(");
            foreach (var parameter in typeDefFunction.Parameters)
            {
                var name = isInterface ? GetFixedTypeDefParameterName(parameter.Name) : GetEscapedTypeDefParameterName(parameter.Name);
                result.Append($"{(parameter.IsReference ? "ref " : "")}{new NativeTypeToCSharpTypingConverter().Convert(null, parameter.NativeType, false)} {name}");
                if (typeDefFunction.Parameters.Last() != parameter)
                {
                    result.Append(", ");
                }
            }
            result.Append($")");

            return result;
        }

        private StringBuilder GenerateFunction(TypeDefFunction typeDefFunction)
        {
            StringBuilder result = new StringBuilder(string.Empty);
            StringBuilder stringsFree = new StringBuilder(string.Empty);
            var fixedTypeDefName = GetFixedTypeDefFunctionName(typeDefFunction.Name);
            result.Append($"{GenerateFunctionDefinition(typeDefFunction, "\t\tpublic ", true, false)}\n");
            result.Append($"\t\t{{\n");
            result.Append($"\t\t\tunsafe {{\n");
            result.Append($"\t\t\t\tif ({fixedTypeDefName} == null) {fixedTypeDefName} = ({GetUnmanagedDelegateType(typeDefFunction)}) funcTable[{typeDefFunction.BaseHash}UL];\n");
            result.Append(GenerateInvoke(typeDefFunction));
            result.Append($"\t\t\t}}\n");
            result.Append($"\t\t}}\n");

            return result;
        }

        private string GenerateInvoke(TypeDefFunction typeDefFunction)
        {
            var returnType = typeDefFunction.ReturnType.NativeType[0];
            var methodName = GetFixedTypeDefFunctionName(typeDefFunction.Name);
            var beforeCall = new StringBuilder();
            var afterCall = new StringBuilder();
            var prependCall = new StringBuilder("\t\t\t\t");

            beforeCall.Append("\t\t\t\tvar success = false;\n");

            var call = new StringBuilder($"{methodName}(&success");

            foreach (var parameter in typeDefFunction.Parameters)
            {
                var argName = GetEscapedTypeDefParameterName(parameter.Name);

                call.Append(", ");
                if (parameter.IsReference && parameter.NativeType == NativeType.String)
                {
                    beforeCall.Append($"\t\t\t\tvar ptr{argName} = MemoryUtils.StringToHGlobalUtf8({argName});\n");
                    beforeCall.Append($"\t\t\t\tvar ref{argName} = ptr{argName};\n");

                    call.Append($"&ref{argName}");

                    afterCall.Append($"\t\t\t\t{argName} = Marshal.PtrToStringUTF8(ref{argName});\n");
                    afterCall.Append($"\t\t\t\tif (ref{argName} != ptr{argName}) freeString(ref{argName});\n");
                    afterCall.Append($"\t\t\t\tMarshal.FreeHGlobal(ptr{argName});\n");
                }
                else if (parameter.IsReference && parameter.NativeType == NativeType.Boolean)
                {
                    beforeCall.Append($"\t\t\t\tvar ref{argName} = (byte) ({argName} ? 1 : 0);\n");
                    call.Append($"&ref{argName}");
                    afterCall.Append($"\t\t\t\t{argName} = ref{argName} == 0 ? false : true;\n");
                }
                else if (parameter.IsReference)
                {
                    beforeCall.Append($"\t\t\t\tvar ref{argName} = {argName};\n");
                    call.Append($"&ref{argName}");
                    afterCall.Append($"\t\t\t\t{argName} = ref{argName};\n");
                }
                else if (parameter.NativeType == NativeType.String)
                {
                    beforeCall.Append($"\t\t\t\tvar ptr{argName} = MemoryUtils.StringToHGlobalUtf8({argName});\n");
                    call.Append($"ptr{argName}");
                    afterCall.Append($"\t\t\t\tMarshal.FreeHGlobal(ptr{argName});\n");
                }
                else if (parameter.NativeType == NativeType.Boolean)
                {
                    call.Append($"(byte) ({argName} ? 1 : 0)");
                }
                else
                {
                    call.Append(argName);
                }
            }

            call.Append(");\n");

            afterCall.Append("\t\t\t\tif (!success) throw new Exception(\"Native execution failed\");\n");

            if (returnType != NativeType.Void)
            {
                prependCall.Append("var result = ");

                if (returnType == NativeType.String)
                {
                    afterCall.Append($"\t\t\t\tvar strResult = Marshal.PtrToStringUTF8(result);\n");
                    afterCall.Append($"\t\t\t\tfreeString(result);\n");
                    afterCall.Append($"\t\t\t\treturn strResult;\n");
                }
                else if (returnType == NativeType.Boolean)
                {
                    afterCall.Append($"\t\t\t\treturn result == 0 ? false : true;\n");
                }
                else
                {
                    afterCall.Append($"\t\t\t\treturn result;\n");
                }
            }

            return beforeCall.ToString() + prependCall.ToString() + call.ToString() + afterCall.ToString();
        }

        private Dictionary<NativeType, string> overloadTypes = new()
        {
            {
                NativeType.Player, "IPlayer"
            },
            {
                NativeType.Ped, "IPlayer"
            },
            {
                NativeType.Vehicle, "IVehicle"
            },
            {
                NativeType.Entity, "IEntity"
            }
        };

        private Dictionary<string, string> GetOverloads(TypeDefFunction typeDefFunction)
        {
            var converter = new NativeTypeToCSharpTypingConverter();
            var overloadedArgsCount = typeDefFunction.Parameters.Count(e => overloadTypes.ContainsKey(e.NativeType) && !e.IsReference);
            if (overloadedArgsCount == 0) return new();

            var dict = new Dictionary<string, string>();
            var overloadedArgsBits = 1 << overloadedArgsCount;
            for (var i = 1; i < overloadedArgsBits; i++)
            {
                var currentDef = new StringBuilder($"{converter.Convert(null, typeDefFunction.ReturnType.NativeType[0], false)} {typeDefFunction.Name.FirstCharToUpper()}(");
                var currentImpl = new StringBuilder($"{typeDefFunction.Name.FirstCharToUpper()}(");
                var j = 0;
                foreach (var arg in typeDefFunction.Parameters)
                {
                    if (!overloadTypes.ContainsKey(arg.NativeType) || arg.IsReference)
                    {
                        currentDef.Append($"{converter.Convert(null, arg.NativeType, arg.IsReference)} {GetFixedTypeDefParameterName(arg.Name)}");
                        currentImpl.Append($"{(arg.IsReference ? "ref " : "")}{GetFixedTypeDefParameterName(arg.Name)}");
                        if (typeDefFunction.Parameters.Last() != arg)
                        {
                            currentDef.Append(", ");
                            currentImpl.Append(", ");
                        }
                        continue;
                    }
                    
                    var isEntity = (i & (1 << j++)) != 0;
                    var type = isEntity ? overloadTypes[arg.NativeType] : converter.Convert(null, arg.NativeType, arg.IsReference);
                    currentDef.Append($"{type} {GetFixedTypeDefParameterName(arg.Name)}");
                    currentImpl.Append(isEntity ? $"{GetFixedTypeDefParameterName(arg.Name)}.ScriptId" : $"{(arg.IsReference ? "ref " : "")}{GetFixedTypeDefParameterName(arg.Name)}");
                    if (typeDefFunction.Parameters.Last() != arg)
                    {
                        currentDef.Append(", ");
                        currentImpl.Append(", ");
                    }
                }
                
                dict[currentDef.ToString() + ")"] = currentImpl.ToString() + ");";
            }

            return dict;
        }

        private StringBuilder GenerateFunctionDocumentation(TypeDefFunction typeDefFunction)
        {
            //When no docs exist
            if (typeDefFunction.ReturnType.NativeType.Count <= 1 &&
                string.IsNullOrEmpty(typeDefFunction.Description) &&
                typeDefFunction.Parameters.All(p => string.IsNullOrEmpty(p.Description) && string.IsNullOrEmpty(typeDefFunction.ReturnType.Description)))
                return new StringBuilder(string.Empty);

            StringBuilder result = new StringBuilder($"\t\t/// <summary>\n");
            if (!string.IsNullOrEmpty(typeDefFunction.Description))
            {
                string[] descriptionLines = typeDefFunction.Description.Split("\n");
                foreach (string descriptionLine in descriptionLines)
                {
                    string sanitizedDescriptionLine = descriptionLine.Replace("/*", string.Empty).Replace("*/", string.Empty).Trim();
                    result.Append($"\t\t/// {sanitizedDescriptionLine}\n");
                }
            }
            result.Append("\t\t/// </summary>\n");

            //Add @remarks in the future?
            foreach (var parameter in typeDefFunction.Parameters)
            {
                if (!string.IsNullOrEmpty(parameter.Description))
                {
                    result.Append($"\t\t/// <param name=\"{GetFixedTypeDefParameterName(parameter.Name)}\">{parameter.Description}</param>\n");
                }
            }

            if (!string.IsNullOrEmpty(typeDefFunction.ReturnType.Description))
            {
                result.Append($"\t\t/// <returns>{typeDefFunction.ReturnType.Description}</returns>\n");
            }
            return result;
        }

        private bool IsParameterNameReservedCSharpKeyWord(string parameterName)
        {
            var reservedKeywords = new List<string>()
            {
                "abstract",
                "as",
                "base",
                "bool",
                "break",
                "byte",
                "case",
                "catch",
                "char",
                "checked",
                "class",
                "const",
                "continue",
                "decimal",
                "default",
                "delegate",
                "do",
                "double",
                "else",
                "enum",
                "event",
                "explicit",
                "extern",
                "false",
                "finally",
                "fixed",
                "float",
                "for",
                "foreach",
                "goto",
                "if",
                "implicit",
                "in",
                "int",
                "interface",
                "internal",
                "is",
                "lock",
                "long",
                "namespace",
                "new",
                "null",
                "object",
                "operator",
                "out",
                "override",
                "params",
                "private",
                "protected",
                "public",
                "readonly",
                "ref",
                "return",
                "sbyte",
                "sealed",
                "short",
                "sizeof",
                "stackalloc",
                "static",
                "string",
                "struct",
                "switch",
                "this",
                "throw",
                "true",
                "try",
                "typeof",
                "uint",
                "ulong",
                "unchecked",
                "unsafe",
                "ushort",
                "using",
                "static",
                "virtual",
                "void",
                "volatile",
                "while"
            };
            return reservedKeywords.Any(k => parameterName.ToLower() == k);
        }
    }
}