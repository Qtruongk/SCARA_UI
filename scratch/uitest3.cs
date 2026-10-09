using System;
using System.Reflection;
using TsRemoteLib;

public class UITester {
    public static void Main() {
        MethodInfo[] methods = typeof(TsRemoteS).GetMethods();
        foreach(var m in methods) {
            if(m.Name.StartsWith("SetPsn") || m.Name.StartsWith("Direct")) {
                Console.WriteLine(m.Name);
            }
        }
    }
}
