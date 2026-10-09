using System;
using System.Reflection;
using TsRemoteLib;

public class Reflector {
    public static void Main() {
        foreach (MethodInfo m in typeof(TsRemoteS).GetMethods()) {
            if (m.Name.StartsWith("Drive")) {
                Console.WriteLine(m.Name);
            }
        }
    }
}
