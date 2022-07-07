namespace Orangebeard.VSTest.TestLogger.ClientExecution
{
    //NOTE: Originally, LaunchContext implemented ILaunchContext, which was an empty extension of ILogContext ... 

    class LaunchContext : ILogContext
    {
        public ILogScope Log { get; set; }
    }
}
