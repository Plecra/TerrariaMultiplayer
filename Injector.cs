using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TerrariaMultiplayer
{
    class Program
    {
        // TerrariaMultiplayer will locate TerrariaServer.exe using steam, patch it with the bundled mods,
        // and run it.
        //
        // - Steam must be running so that we can ask it where to find Terraria
        //
        // `TerrariaMultiplayer` shares dependencies with the official game:
        // - .NET 4.0.0.0
        // - Steamworks 20.0.0.0
        static void Main(string[] args)
        {
            var gamedir = args.Length < 1 ? FindTerraria() : args[0];
            var def = AssemblyDefinition.ReadAssembly(gamedir + "/TerrariaServer.exe");

            // Crack open the visibility of all Terraria types.
            var types = new Stack<TypeDefinition>(def.MainModule.Types);
            while (types.Count > 0)
            {
                var type = types.Pop();
                foreach(var ntype in type.NestedTypes)
                {
                    types.Push(ntype);
                }
                foreach(var method in type.Methods)
                {
                    method.IsPublic = true;
                }
            }

#if !RUNTIME
            // Generate symbols for development: we need to extract them for use in the runtime code
            def.Write("TerrariaServer.exe", new WriterParameters
            {
                WriteSymbols = true,
                SymbolWriterProvider = new PortablePdbWriterProvider(),
            });
#else
            var injector = new Injector() { def = def, action_scope = MakeReference(typeof(Action).Assembly) };

            var modref = MakeReference(typeof(Program).Assembly);
            def.MainModule.AssemblyReferences.Add(modref);

            var hooksref = new TypeReference("TerrariaMultiplayer", "Hooks", def.MainModule, modref);
            foreach (var method in typeof(Hooks).GetMethods())
            {
                // A silly little parsing scheme I've chosen - hooks are defined as Hooks.TYPE_METHOD
                var i = method.Name.IndexOf('_');
                if (i == -1) continue;
                var vanilla_method_name = method.Name.Substring(i + 1);
                var type = def.MainModule.Types.Where(t => t.Name == method.Name.Substring(0, i)).First();

                var vanilla_method = type.Methods.Where(m => m.Name == vanilla_method_name).First();
                injector.Install(
                    vanilla_method,
                    new MethodReference(method.Name, vanilla_method.ReturnType, hooksref),

                    // Unfortunately, we need to manually reference all `Signatures`.
                    // This is because System.Reflection doesnt allow us to inspect the parameter type
                    //  without loading the Terraria namespace. (We would need `method.GetParameter(last)`, instead
                    //  of `method.GetParameters()` which risks needing to refer to a type declared elsewhere)
                    new TypeReference("", vanilla_method_name, def.MainModule, modref)
                    {
                        DeclaringType = hooksref
                    });
            }

            // Load the rewritten assembly as TerrariaServer
            var mStream = new System.IO.MemoryStream();
            def.Write(mStream);
            var assembly = Assembly.Load(mStream.GetBuffer());
            AppDomain.CurrentDomain.AssemblyResolve += (object sender, ResolveEventArgs sargs) => {
                return new AssemblyName(sargs.Name).Name == "TerrariaServer" ? assembly : null;
            };

            Entrypoint.Run(gamedir);
#endif
        }
        static string FindTerraria()
        {
            if (!Steamworks.SteamAPI.Init()) throw new Exception("I can't connect to Steam. Chances are you need to open it and login.");
            string gamedir;
            Steamworks.SteamApps.GetAppInstallDir(new Steamworks.AppId_t(105600), out gamedir, 2048);
            return gamedir;
        }
        static AssemblyNameReference MakeReference(Assembly assembly)
        {
            var name = assembly.GetName();
            return new AssemblyNameReference(name.Name, name.Version)
            {
                PublicKeyToken = name.GetPublicKeyToken(),
                Culture = name.CultureInfo.Name,
                HashAlgorithm = (Mono.Cecil.AssemblyHashAlgorithm)name.HashAlgorithm,
            };
        }

    }

    struct Injector
    {
        public AssemblyDefinition def;
        public AssemblyNameReference action_scope;

        public void Install(MethodDefinition vanilla_method, MethodReference detour, TypeReference delegate_type)
        {
            var orig_impl = CloneMethod("orig_", vanilla_method);

            // Reset the code for the method
            vanilla_method.Body = new Mono.Cecil.Cil.MethodBody(vanilla_method);
            var il = vanilla_method.Body.GetILProcessor();
            
            // forward all the method's parameters to the injected detour
            byte arg_idx = 0;
            foreach (var param in MethodParameters(vanilla_method))
            {
                detour.Parameters.Add(param);
                il.Emit(OpCodes.Ldarg_S, arg_idx++);
            }

            // and pass the original implementation as the last argument
            detour.Parameters.Add(new ParameterDefinition(delegate_type));
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, orig_impl);
            il.Emit(OpCodes.Newobj, new MethodReference(".ctor", def.MainModule.TypeSystem.Void, delegate_type)
            {
                HasThis = true,
                Parameters =
                    {
                        new ParameterDefinition(def.MainModule.TypeSystem.Object),
                        new ParameterDefinition(def.MainModule.TypeSystem.IntPtr)
                    }
            });

            // Tail calling the detour :)
            il.Emit(OpCodes.Tail);
            il.Emit(OpCodes.Call, detour);
            il.Emit(OpCodes.Ret);
        }
        MethodDefinition CloneMethod(string prefix, MethodDefinition method)
        {
            var clone = new MethodDefinition(prefix + method.Name, Mono.Cecil.MethodAttributes.Static, method.ReturnType)
            {
                DeclaringType = method.DeclaringType,
                Body = method.Body,
            };
            method.DeclaringType.Methods.Add(clone);
            foreach (var param in MethodParameters(method)) clone.Parameters.Add(param);
            return clone;
        }
        IEnumerable<ParameterDefinition> MethodParameters(MethodReference method)
        {
            var implicit_parameters = method.HasThis ? new[] { method.DeclaringType } : new TypeReference[] { };
            return implicit_parameters.Concat(method.Parameters.Select(v => v.ParameterType)).Select(t => new ParameterDefinition(t));
        }
    }
}
