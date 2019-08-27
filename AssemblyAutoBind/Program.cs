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
            var list = files.Select(e =>
            {
                try
                {
                    Console.WriteLine(e);
                    return ModuleDefMD.Load(e);
                }
                catch (Exception exception)
                {
                    Console.WriteLine(e + " " + exception);
                    return null;
                }
            }).Where(e => e != null).ToList();

            var tuples = list.Where(e => e.IsStrongNameSigned).Select(e => (Name: e.Assembly.Name.String + e.Assembly.PublicKeyToken, e.Assembly.Version)).ToList();
            var rList = new List<(string name, string token, Version version)>();
            foreach (var moduleDefMd in list)
            {
                foreach (var assemblyRef in moduleDefMd.GetAssemblyRefs().Where(e => !e.PublicKeyOrToken.IsNullOrEmpty))
                {
                    var s = assemblyRef.Name.String + assemblyRef.PublicKeyOrToken;
                    if (tuples.Any(e => e.Name.Equals(s, StringComparison.OrdinalIgnoreCase) && e.Version != assemblyRef.Version))
                    {
                        rList.Add((assemblyRef.Name, assemblyRef.PublicKeyOrToken.ToString(), assemblyRef.Version));
                    }
                }
            }
            var node = document.GetElementsByTagName("runtime").OfType<XmlNode>().FirstOrDefault();
            if (node == null)
            {
                node = document.CreateElement("runtime");
                document.GetElementsByTagName("configuration")[0].AppendChild(node);
            }
            var oldList = new List<(string name, string token, Version version)>();
            foreach (var assemblyBinding1 in node.ChildNodes.OfType<XmlNode>().Where(e => e.Name == "assemblyBinding"))
            {
                foreach (var dependentAssembly1 in assemblyBinding1.ChildNodes.OfType<XmlNode>().Where(e => e.Name == "dependentAssembly"))
                {
                    var nodes = dependentAssembly1.ChildNodes.OfType<XmlNode>();
                    var assemblyIdentity = nodes.FirstOrDefault(e => e.Name == "assemblyIdentity");
                    var bindingRedirect = nodes.FirstOrDefault(e => e.Name == "bindingRedirect");
                    var value = bindingRedirect.Attributes["oldVersion"].Value.Split('-')[1];
                    var value2 = bindingRedirect.Attributes["newVersion"].Value;
                    rList.Add((assemblyIdentity.Attributes["name"].Value, assemblyIdentity.Attributes["publicKeyToken"].Value.ToLower(), Version.Parse(value)));
                    rList.Add((assemblyIdentity.Attributes["name"].Value, assemblyIdentity.Attributes["publicKeyToken"].Value.ToLower(), Version.Parse(value2)));
                    var md = list.FirstOrDefault(e => e.Assembly.Name.String.Equals(assemblyIdentity.Attributes["name"].Value) && e.Assembly.PublicKeyToken.ToString() == assemblyIdentity.Attributes["publicKeyToken"].Value.ToLower());
                    if (md == null)
                    {
                        oldList.Add((assemblyIdentity.Attributes["name"].Value, assemblyIdentity.Attributes["publicKeyToken"].Value.ToLower(), Version.Parse(value2)));
                    }
                }
            }
            node.RemoveAll();
            /**/
            var aList = document.CreateElement("assemblyBinding", "urn:schemas-microsoft-com:asm.v1");
            node.AppendChild(aList);

            foreach (var grouping in rList.GroupBy(e => (e.name, e.token)))
            {
                var versions = grouping.Select(e => e.version).ToList();
                var md = list.FirstOrDefault(e => e.Assembly.Name.String.Equals(grouping.Key.name) && e.Assembly.PublicKeyToken.ToString() == grouping.Key.token);
                Version k;
                if (md == null)
                {
                    // Console.WriteLine(grouping.Key.name + " " + grouping.Key.token);
                    k = oldList.FirstOrDefault(e => e.name == grouping.Key.name && e.token == grouping.Key.token).version;
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
    }
}