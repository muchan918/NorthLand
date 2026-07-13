namespace NorthLand.Combat
{
    public struct DamageInfo
    {
        public float Amount;
        public IAttacker Source;

        public DamageInfo(float amount, IAttacker source)
        {
            Amount = amount;
            Source = source;
        }
    }
}
