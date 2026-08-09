using System; using System.Linq; using System.Reflection;
class R { static void Main(){
 try{ new Opc.Ua.NodeId(0);}catch{}
 var all=AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=>{try{return a.GetTypes();}catch(ReflectionTypeLoadException e){return e.Types.Where(t=>t!=null);}}).ToList();
 foreach(var t in all.Where(x=>x!=null && x.Name.Contains("Builder") && (x.Namespace??"").StartsWith("Opc.Ua"))) Console.WriteLine("BUILDER: "+t.FullName + " ifaces="+string.Join(",",t.GetInterfaces().Select(i=>i.Name)));
 var ps=all.FirstOrDefault(x=>x.Name=="PropertyState`1");
 if(ps!=null){ Console.WriteLine("--- PropertyState<T>.With ---"); foreach(var m in ps.GetMethods().Where(m=>m.Name=="With")){ var g=m.IsGenericMethod?"<"+string.Join(",",m.GetGenericArguments().Select(a=>a.Name+":"+string.Join("+",a.GetGenericParameterConstraints().Select(c=>c.Name))))+">":""; Console.WriteLine("  With"+g+"("+string.Join(", ",m.GetParameters().Select(p=>p.ParameterType.Name))+")"); } }
 var ivb=all.FirstOrDefault(x=>x.Name=="IVariantBuilder`1");
 if(ivb!=null) foreach(var a in all.Where(x=>x!=null && ivb.IsAssignableFrom(x))) Console.WriteLine("IVariantBuilder impl: "+x_name(x));
}
 static string x_name(Type t)=>t.FullName;
}
