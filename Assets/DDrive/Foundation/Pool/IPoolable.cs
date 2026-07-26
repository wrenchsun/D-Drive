namespace DDrive.Foundation.Pool
{
    // Return 時に呼ばれ、Trail/Particle 等の残留状態をリセットする差し込み口。
    public interface IPoolable
    {
        void OnReturn();
    }
}
