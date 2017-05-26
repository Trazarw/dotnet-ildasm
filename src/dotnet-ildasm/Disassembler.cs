﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace DotNet.Ildasm
{
    sealed class Disassembler
    {
        private readonly IOutputWriter _outputWriter;
        private readonly CommandOptions _options;
        private readonly ItemFilter _itemFilter;

        internal Disassembler(IOutputWriter outputWriter, CommandOptions options, ItemFilter itemFilter)
        {
            _outputWriter = outputWriter;
            _options = options;
            _itemFilter = itemFilter;
        }

        internal void Execute()
        {
            var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(_options.FilePath);

            WriteAssemblyData(assembly);
        }

        private void WriteExterns(AssemblyDefinition assembly)
        {
            foreach (var reference in assembly.MainModule.AssemblyReferences)
            {
                _outputWriter.WriteLine();
                _outputWriter.WriteLine($".assembly extern { reference.Name }");
                _outputWriter.WriteLine("{");
                _outputWriter.WriteLine($".publickeytoken ( { ExtractValueInHex(reference.PublicKeyToken) } )");
                _outputWriter.WriteLine($".ver { reference.Version.Major }:{ reference.Version.Minor }:{ reference.Version.Revision }:{ reference.Version.Build }");
                _outputWriter.WriteLine("}");
            }
        }

        private string ExtractValueInHex(Byte[] data)
        {
            return BitConverter.ToString(data);
        }

        private void WriteAssemblyData(AssemblyDefinition assembly)
        {
            if (!_itemFilter.HasFilter)
            {
                WriteExterns(assembly);
                WriteAssemblySection(assembly);
            }

            foreach (var module in assembly.Modules)
            {
                if (!_itemFilter.HasFilter)
                    HandleModule(module);

                foreach (var type in module.Types)
                {
                    if (string.Compare(type.Name, "<Module>") == 0)
                        continue;

                    if (string.IsNullOrEmpty(_itemFilter.Class) || string.Compare(type.Name, _itemFilter.Class, StringComparison.CurrentCulture) == 0)
                        HandleType(type);
                }
            }
        }

        private void WriteAssemblySection(AssemblyDefinition assembly)
        {
            _outputWriter.WriteLine();
            _outputWriter.WriteLine($".assembly '{assembly.Name.Name}'");
            _outputWriter.WriteLine("{");

            foreach (var customAttribute in assembly.CustomAttributes)
            {
                if (String.Compare(customAttribute.AttributeType.Name, "DebuggableAttribute",
                        StringComparison.CurrentCultureIgnoreCase) == 0)
                {
                    _outputWriter.WriteLine(
                        "// The following custom attribute is added automatically for debugging purposes, do not uncomment. ");
                    _outputWriter.Write("//");
                }

                //TODO: Add custom attributes parameter values #4
                //TODO: Signature to use IL types #2
                //TODO: External Types should always be preceded by their assembly names #6
                _outputWriter.WriteLine($".custom instance {customAttribute.Constructor.ToString()} = ( { ExtractValueInHex(customAttribute) } )");
            }

            _outputWriter.WriteLine($".hash algorithm 0x{assembly.Name.HashAlgorithm.ToString("X")}");
            _outputWriter.WriteLine(
                $".ver {assembly.Name.Version.Major}:{assembly.Name.Version.Minor}:{assembly.Name.Version.Revision}:{assembly.Name.Version.Build}");
            _outputWriter.WriteLine("}");
        }

#if NETCORE_2
        byte[] ObjectToByteArray(object obj)
        {
            if (obj == null)
                return null;

            var bf = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }
#else
        byte[] ObjectToByteArray(object obj)
        {
            return new byte[] { };
        }
#endif

        private string ExtractValueInHex(CustomAttribute customAttribute)
        {
            if (customAttribute.IsResolved && customAttribute.ConstructorArguments != null)
            {
                var customAttributeArgument = customAttribute.ConstructorArguments.FirstOrDefault();
                byte[] bytes = ObjectToByteArray(customAttributeArgument);
                return ExtractValueInHex(bytes);
            }

            return string.Empty;
        }

        private void HandleType(TypeDefinition type)
        {
            _outputWriter.WriteLine();
            WriteTypeSignature(type);
            _outputWriter.WriteLine();
            _outputWriter.WriteLine("{");

            foreach (var method in type.Methods)
            {
                if (string.IsNullOrEmpty(_itemFilter.Method) || string.Compare(method.Name, _itemFilter.Method, StringComparison.CurrentCulture) == 0)
                    HandleMethod(method);
            }

            _outputWriter.WriteLine();
            _outputWriter.WriteLine($"}} // End of class {type.FullName}");
        }

        private void HandleMethod(MethodDefinition method)
        {
            _outputWriter.WriteLine();
            WriteMethodSignature(method);
            _outputWriter.WriteLine();
            _outputWriter.WriteLine("{");

            if (method.DeclaringType.Module.EntryPoint == method)
                _outputWriter.WriteLine(".entrypoint");

            if (method.HasBody)
            {
                _outputWriter.WriteLine($"// Code size {method.Body.CodeSize}");
                _outputWriter.WriteLine($".maxstack {method.Body.MaxStackSize}");

                var ilProcessor = method.Body.GetILProcessor();
                foreach (var instruction in ilProcessor.Body.Instructions)
                {
                    //TODO: Use IL types instead of .Net types #2
                    //TODO: External Types should always be preceded by their assembly names #6
                    _outputWriter.WriteLine(instruction.ToString());
                }
            }
            _outputWriter.WriteLine($"}}// End of method {method.FullName}");
        }

        private void WriteTypeSignature(TypeDefinition type)
        {
            _outputWriter.Write(".class");

            if (type.IsPublic)
                _outputWriter.Write(" public");
            else
                _outputWriter.Write(" private");

            if (type.IsSequentialLayout)
                _outputWriter.Write(" sequential");

            if (type.IsInterface)
                _outputWriter.Write(" interface");

            if (type.IsAbstract)
                _outputWriter.Write(" abstract");

            if (type.IsAutoLayout)
                _outputWriter.Write(" auto");

            if (type.IsAnsiClass)
                _outputWriter.Write(" ansi");

            if (type.IsAbstract)
                _outputWriter.Write(" sealed");

            if (type.IsBeforeFieldInit)
                _outputWriter.Write(" beforefieldinit");

            //TODO: Signature to use IL types #2
            _outputWriter.Write($" {type.FullName}");

            if (type.BaseType != null)
                //TODO: External Types should always be preceded by their assembly names #6
                _outputWriter.Write($" extends {type.BaseType.FullName}");

            if (type.HasInterfaces)
                //TODO: External Types should always be preceded by their assembly names #6
                _outputWriter.Write($" implements { string.Join(", ", type.Interfaces.Select(x => x.InterfaceType.FullName )) }");
        }

        private void WriteMethodSignature(MethodDefinition method)
        {
            _outputWriter.Write(".method");

            if (method.IsPublic)
                _outputWriter.Write(" public");
            else if (method.IsPrivate)
                _outputWriter.Write(" private");

            if (method.IsHideBySig)
                _outputWriter.Write(" hidebysig");

            if (method.IsSpecialName)
                _outputWriter.Write(" specialname");

            if (method.IsRuntimeSpecialName)
                _outputWriter.Write(" rtspecialname");

            if (!method.IsStatic)
                _outputWriter.Write(" instance");

            //TODO: Signature to use IL types #2
            _outputWriter.Write($" {method.FullName}");

            if (method.IsManaged)
                _outputWriter.Write(" cil managed");
        }

        private void HandleModule(ModuleDefinition module)
        {
            _outputWriter.WriteLine("");
            _outputWriter.WriteLine($".module '{ module.Assembly.Name.Name }'");
            _outputWriter.WriteLine($"// MVID: {{{module.Mvid}}}");

            //TODO: Load module information #1
            _outputWriter.WriteLine($"// .imagebase 0x000000 (Currently not supported)");
            _outputWriter.WriteLine($"// .file alignment 0x000000 (Currently not supported)");
            _outputWriter.WriteLine($"// .stackreserve 0x000000 (Currently not supported)");

            //TODO: Load subsystem from actual memory instead of assume it #1
            if (module.Kind == ModuleKind.Console || module.Kind == ModuleKind.Windows)
                _outputWriter.WriteLine($"//.subsystem 0x{ GetSubsystem(module.Kind).ToString("x3") }");

            _outputWriter.WriteLine($"// .cornflags 0x000000 (Currently not supported)");
            _outputWriter.WriteLine($"// image base:  ");
        }

        private uint GetSubsystem(ModuleKind moduleKind)
        {
            if (moduleKind == ModuleKind.Console)
                return 0x003;

            if (moduleKind == ModuleKind.Windows)
                return 0x002;

            return 0;
        }
    }
}
