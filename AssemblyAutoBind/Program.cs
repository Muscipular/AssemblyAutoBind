using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using dnlib.DotNet;

namespace AssemblyAutoBind
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = args[0];
            var bin = args[1];
            AssemblyConfig(config, bin);
        }

        private static void AssemblyConfig(string config, string bin)
        {
            Console.WriteLine("config: " + Path.GetFullPath(config));
            Console.WriteLine("dll path: " + Path.GetFullPath(bin));
            var document = new XmlDocument();
            document.Load(config);
            var files = Directory.GetFiles(bin).Where(e => Regex.IsMatch(e, @"\.(dll|exe)$", RegexOptions.IgnoreCase));

            var assemblyResolver = new AssemblyResolver();
            var moduleContext = new ModuleContext(assemblyResolver);
            assemblyResolver.DefaultModuleContext = moduleContext;
            assemblyResolver.EnableTypeDefCache = true;
            var dllList = files.Select(e =>
            {
                try
                {
                    // Console.WriteLine(e);
                    return ModuleDefMD.Load(e, moduleContext);
                }
                catch (Exception exception)
                {
                    // Console.WriteLine(e + " " + exception);
                    return null;
                }
            }).Where(e => e != null).ToList();

            var signedDll = dllList.Where(e => e.IsStrongNameSigned)
                    .Select(e => (name: e.Assembly.Name.String, token: e.Assembly.PublicKeyToken.ToString(), version: e.Assembly.Version))
                    .ToList();
            var confusedList = new List<(string name, string token, Version version)>();

            var moduleDefs = ResolveAllAssembly(dllList, assemblyResolver).ToList();
            foreach (var moduleDefMd in moduleDefs)
            {
                foreach (var assemblyRef in moduleDefMd.GetAssemblyRefs().Where(e => !e.PublicKeyOrToken.IsNullOrEmpty))
                {
                    var name = assemblyRef.Name.String;
                    var token = assemblyRef.PublicKeyOrToken.ToString();
                    if (signedDll.Any(e => e.name == name && e.token == token && e.version != assemblyRef.Version))
                    {
                        confusedList.Add((assemblyRef.Name, token, assemblyRef.Version));
                    }
                }
            }
            var node = document.GetElementsByTagName("runtime").OfType<XmlNode>().FirstOrDefault();
            if (node == null)
            {
                node = document.CreateElement("runtime");
                document.GetElementsByTagName("configuration")[0].AppendChild(node);
            }
            var origConfigList = new List<(string name, string token, Version version)>();
            foreach (var assemblyBinding1 in node.ChildNodes.OfType<XmlNode>().ToList().Where(e => e.Name == "assemblyBinding"))
            {
                foreach (var dependentAssembly1 in assemblyBinding1.ChildNodes.OfType<XmlNode>().Where(e => e.Name == "dependentAssembly"))
                {
                    var nodes = dependentAssembly1.ChildNodes.OfType<XmlNode>();
                    var assemblyIdentity = nodes.FirstOrDefault(e => e.Name == "assemblyIdentity");
                    var bindingRedirect = nodes.FirstOrDefault(e => e.Name == "bindingRedirect");
                    var oldVersion = Version.Parse(bindingRedirect.Attributes["oldVersion"].Value.Split('-')[1]);
                    var newVersion = Version.Parse(bindingRedirect.Attributes["newVersion"].Value);
                    var name = assemblyIdentity.Attributes["name"].Value;
                    var token = assemblyIdentity.Attributes["publicKeyToken"].Value.ToLower();
                    confusedList.Add((name, token, oldVersion));
                    confusedList.Add((name, token, newVersion));
                    if (!signedDll.Any(e => e.name == name && e.token == token))
                    {
                        origConfigList.Add((name, token, newVersion));
                    }
                }
                node.RemoveChild(assemblyBinding1);
            }
            // foreach (var tuple in signedDll)
            // {
            //     confusedList.Add(tuple);
            // }
            // node.RemoveAll();
            /**/
            var aList = document.CreateElement("assemblyBinding", "urn:schemas-microsoft-com:asm.v1");
            node.AppendChild(aList);

            foreach (var grouping in confusedList.GroupBy(e => (e.name, e.token)))
            {
                var versions = grouping.Select(e => e.version).ToList();
                var md = dllList.FirstOrDefault(e => e.Assembly.Name.String.Equals(grouping.Key.name) && e.Assembly.PublicKeyToken.ToString() == grouping.Key.token);
                Version k;
                if (md == null)
                {
                    // Console.WriteLine(grouping.Key.name + " " + grouping.Key.token);
                    k = origConfigList.FirstOrDefault(e => e.name == grouping.Key.name && e.token == grouping.Key.token).version;
                }
                else
                {
                    k = md.Assembly.Version;
                }
                versions.Add(k);
                Console.WriteLine($"{grouping.Key.name} {grouping.Key.token} 0.0.0.0-{versions.Max()} => {k}");
                var x1 = document.CreateElement("dependentAssembly");
                var e1 = document.CreateElement("assemblyIdentity");
                e1.SetAttribute("name", grouping.Key.name);
                e1.SetAttribute("publicKeyToken", grouping.Key.token);
                e1.SetAttribute("culture", "neutral");
                x1.AppendChild(e1);
                var e2 = document.CreateElement("bindingRedirect");
                e2.SetAttribute("oldVersion", $"0.0.0.0-{versions.Max()}");
                e2.SetAttribute("newVersion", k.ToString());
                x1.AppendChild(e2);
                x1.Normalize();
                aList.AppendChild(x1);
            }
            try
            {
                File.Copy(config, config + ".bak.config");
            }
            catch (Exception e)
            {
            }
            var memoryStream = new MemoryStream();
            document.Save(new StreamWriter(memoryStream, Encoding.UTF8));
            var s1 = Encoding.UTF8.GetString(memoryStream.ToArray());
            File.WriteAllBytes(config, Encoding.UTF8.GetBytes(s1.Replace(" xmlns=\"\"", "")));
        }

        private static IEnumerable<ModuleDef> ResolveAllAssembly(IEnumerable<ModuleDef> dllList, IAssemblyResolver resolver)
        {
            List<ModuleDef> defs = new List<ModuleDef>();
            var moduleDefs = dllList.ToList();
            while (moduleDefs.Any())
            {
                var moduleDef = moduleDefs[0];
                moduleDefs.RemoveAt(0);
                if (defs.Any(e => e.Name == moduleDef.Name && e.Assembly.Version == moduleDef.Assembly.Version))
                {
                    continue;
                }
                defs.Add(moduleDef);
                moduleDefs.AddRange(moduleDef.GetAssemblyRefs().Select(e => resolver.Resolve(e, moduleDef)?.ManifestModule).Where(e => e != null));
            }
            return defs;
        }
    }
}