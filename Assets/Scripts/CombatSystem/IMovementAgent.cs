namespace NorthLand.Combat
{
    public interface IMovementAgent
    {
        bool IsStopped { get; set; }

        void SetMoveSpeed(float moveSpeed);
    }
}
