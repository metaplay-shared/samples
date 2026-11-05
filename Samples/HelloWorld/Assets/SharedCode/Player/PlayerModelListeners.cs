namespace Game.Logic
{
    public interface IPlayerModelServerListener
    {
    }

    public interface IPlayerModelClientListener
    {
    }

    public class EmptyPlayerModelServerListener : IPlayerModelServerListener
    {
        public static readonly EmptyPlayerModelServerListener Instance = new EmptyPlayerModelServerListener();
    }

    public class EmptyPlayerModelClientListener : IPlayerModelClientListener
    {
        public static readonly EmptyPlayerModelClientListener Instance = new EmptyPlayerModelClientListener();
    }
}
