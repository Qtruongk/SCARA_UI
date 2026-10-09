using System;
using System.Reflection;
using TsRemoteLib;

public class UITester {
    public static void Main() {
        MethodInfo[] methods = typeof(TsRemoteS).GetMethods();
        foreach(var m in methods) {
            if(m.Name == "DirectDo") {
                Console.Write(m.Name + "(");
                ParameterInfo[] p = m.GetParameters();
                for(int i=0; i<p.Length; i++) {
                    Console.Write(p[i].ParameterType.Name + " " + p[i].Name);
                    if(i < p.Length - 1) Console.Write(", ");
                }
                Console.WriteLine(")");
            }
        }
    }
}
