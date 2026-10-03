namespace GitCheckoutManager.Services
{
    /// <summary>The running app's version, without the "+commit" build metadata.</summary>
    public static class AppVersion
    {
        public static string Current { get; } = Read();

        private static string Read()
        {
            var info = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            return info?.Split('+')[0] ?? "?";
        }
    }
}
