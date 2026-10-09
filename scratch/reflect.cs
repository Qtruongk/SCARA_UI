using System;
using System.Reflection;
using TsRemoteLib;

public class Reflector {
    public static void Main() {
        foreach (MethodInfo m in typeof(TsRemoteS).GetMethods()) {
            if (m.Name.StartsWith("Move")) {
                Console.Write(m.Name + "(");
                ParameterInfo[] pars = m.GetParameters();
                for (int i = 0; i < pars.Length; i++) {
                    Console.Write(pars[i].ParameterType.Name + " " + pars[i].Name);
                    if (i < pars.Length - 1) Console.Write(", ");
                }
                Console.WriteLine(")");
            }
        }
    }
}
