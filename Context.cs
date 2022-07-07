namespace Orangebeard.VSTest.TestLogger
{
    public class Context
    {
        public static NewTestContext Current { get; set; } = new NewTestContext(null, null);
    }
}
