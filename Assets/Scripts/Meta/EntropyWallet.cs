namespace KernelPanic.Meta
{
    /// <summary>
    /// Tracks the player's persistent entropy currency balance.
    /// </summary>
    public sealed class EntropyWallet
    {
        public int Balance { get; private set; }

        public void SetBalance(int amount)
        {
            Balance = amount < 0 ? 0 : amount;
        }

        public void Add(int amount)
        {
            Balance += amount;
        }

        public bool Spend(int amount)
        {
            if (Balance < amount)
            {
                return false;
            }

            Balance -= amount;
            return true;
        }
    }
}
