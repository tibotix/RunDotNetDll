using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using System.CommandLine;

namespace RunDotNetDll
{
    class Program
    {
        private static Object? CreateObject(Type type)
        {
            Object? result = null;
            if (type.IsArray)
            {
                if (type.GetElementType() is Type elementType)
                {
                    result = Array.CreateInstance(elementType, 0);
                }
            }
            else if (!type.IsAbstract)
            {
                try
                {
                    result = Activator.CreateInstance(type);
                }
                catch
                {
                    // unable to create the given object add an uninitialized object
                    result = FormatterServices.GetUninitializedObject(type);
                }
            }
            return result;
        }

        private static IEnumerable<Type> GetAllTypes(String dll)
        {
            return Assembly.LoadFrom(dll).GetTypes();
        }

        private static IEnumerable<MethodBase> GetAllMethods(String dll, Boolean filter)
        {
            var methods = GetAllTypes(dll).SelectMany(t => t.GetMethods());
            var modules = Assembly.LoadFrom(dll).GetModules();

            foreach (var module in modules)
            {
                foreach (var type in module.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        MethodBase? methodBase = null;
                        try
                        {
                            methodBase = module.ResolveMethod(method.MetadataToken);
                        }
                        catch { }
                        if (methodBase != null)
                        {
                            yield return methodBase;
                        }
                    }
                }
            }
        }

        private static String GetFullMethodName(MethodBase methodBase)
        {
            var moduleName = methodBase.DeclaringType == null ? "<Module>" : methodBase.DeclaringType.FullName;
            return String.Format("{0}.{1}", moduleName, methodBase.Name);
        }

        private static Boolean IsTargetMethod(MethodBase methodBase, String entryPointName, Int32 metadataToken)
        {
            return
                methodBase.MetadataToken == metadataToken ||
                GetFullMethodName(methodBase).Equals(entryPointName, StringComparison.OrdinalIgnoreCase);
        }

        private static Boolean TryRunWindowsForm(String dll, String entryPointName)
        {
            var success = false;
            var formType =
                GetAllTypes(dll)
                .Where(type => type.Module.Name.Equals(type.Assembly.ManifestModule.Name))
                .Where(type => typeof(Form).IsAssignableFrom(type) && type.FullName != null && type.FullName.Equals(entryPointName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (formType != null)
            {
                if (Activator.CreateInstance(formType) is Form form)
                {
                    form.Show();
                    success = true;
                }
            }

            return success;
        }

        private static void RunDllMethod(String dll, String entryPointName)
        {
            Object? instance = null;
            var metadataToken = -1;
            if (entryPointName.StartsWith("@"))
            {
                var cleanToken = entryPointName.TrimStart('@');
                if (cleanToken.StartsWith("0x"))
                    metadataToken = Convert.ToInt32(cleanToken, 16);
                else
                    metadataToken = Convert.ToInt32(cleanToken, 10);
            }

            var entryPoint =
                GetAllMethods(dll, false)
                .Where(methodBase => IsTargetMethod(methodBase, entryPointName, metadataToken))
                .FirstOrDefault();

            if (entryPoint == null)
            {
                Console.Error.WriteLine("No EntryPoint defined for the Dll: " + dll);
                Environment.Exit(3);
            }

            // if it is an instance method, try to create an object by invoking the default constructor
            if (!entryPoint.IsStatic && entryPoint.DeclaringType is Type t)
            {
                instance = Activator.CreateInstance(t);
            }

            // create the list of parameters to pass
            var parameters = new List<Object?>();
            foreach (var parameterInfo in entryPoint.GetParameters())
            {
                parameters.Add(CreateObject(parameterInfo.ParameterType));
            }

            // invoke the entry point
            entryPoint.Invoke(instance, parameters.ToArray());
        }

        private static void ShowallMethods(String dll)
        {
            var allMethods = GetAllMethods(dll, true);

            Console.WriteLine("[+] Methods");
            foreach (var method in allMethods)
            {
                Console.WriteLine("\t{0} (0x{1}) - {2}", method.MetadataToken, method.MetadataToken.ToString("X"), GetFullMethodName(method));
            }
        }


        static int Main(string[] args)
        {


            var assemblyArgument = new Argument<FileInfo>("assembly", "The Assembly to run.");
            var methodArgument = new Argument<string>(name: "method",
                description: "The method to call. You can specify the metadata token too. eg: Mynaspace.MyClass.EntryPoint or @0x06000001",
                getDefaultValue: () => "");
            var rootCommand = new RootCommand("-= Run a specific method of a .NET Assembly =-")
            {
                assemblyArgument,
                methodArgument
            };

            var preloadFiles = new Option<FileInfo[]>(
                name: "--preload",
                description: "An assembly to preload.");
            preloadFiles.AddAlias("-p");
            rootCommand.AddOption(preloadFiles);

            rootCommand.SetHandler((context) =>
            {
                FileInfo[]? preloads = context.ParseResult.GetValueForOption(preloadFiles);
                FileInfo assembly = context.ParseResult.GetValueForArgument(assemblyArgument);
                string method = context.ParseResult.GetValueForArgument(methodArgument);

                if (!assembly.Exists)
                {
                    Console.Error.WriteLine("Unable to find file: " + assembly.FullName);
                    context.ExitCode = 2;
                }
                

                Console.WriteLine("[+] DLL: " + assembly.FullName);
                if (preloads != null) {
                    foreach(var preload in preloads)
                    {
                        Console.Write("[*] Preloading DLL: " + preload.FullName);
                        if (!preload.Exists)
                        {
                            Console.WriteLine(" (not found)");
                            continue;
                        }
                        try
                        {
                            Assembly.LoadFrom(preload.FullName);
                            Console.WriteLine();
                        } catch (Exception e)
                        {
                            Console.WriteLine(" (error: " + e.ToString() + ")");
                        }
                    }
                }

                if (String.IsNullOrWhiteSpace(method))
                {
                    ShowallMethods(assembly.FullName);
                }
                else
                {
                    if (!TryRunWindowsForm(assembly.FullName, method))
                    {
                        RunDllMethod(assembly.FullName, method);
                    }

                    Console.WriteLine("[+] Press Enter to Exit");
                    Console.ReadLine();
                }
            });

            return rootCommand.Invoke(args);
        }    
    }
}
