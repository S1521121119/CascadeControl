using System;
using System.Runtime.CompilerServices;

namespace CascadeControl.Graphs
{
    /// <summary>
    /// 一條有向邊 <see cref="From"/> → <see cref="To"/>。以兩個頂點的參照比較相等。
    /// </summary>
    /// <remarks>
    /// 不用 "From->To" 這類字串當鍵：名稱本身含有 "->" 時，("a->b", "c") 與 ("a", "b->c") 會組成同一個字串。
    /// 邊沒有值（無權重圖）；<see cref="MenuEdge"/> 只是建立當下的快照，邊被移除後它仍然存在，但 <see cref="MenuGraph.IsAdjacent"/> 會回傳 false。
    /// </remarks>
    public readonly struct MenuEdge : IEquatable<MenuEdge>
    {
        /// <summary>
        /// 建立有向邊。
        /// </summary>
        /// <param name="from">起點。</param>
        /// <param name="to">終點。</param>
        /// <exception cref="ArgumentNullException"><paramref name="from"/> 或 <paramref name="to"/> 為 null。</exception>
        public MenuEdge(MenuVertex from, MenuVertex to)
        {
            From = from ?? throw new ArgumentNullException(nameof(from));
            To = to ?? throw new ArgumentNullException(nameof(to));
        }

        /// <summary>
        /// 起點。
        /// </summary>
        public MenuVertex From { get; }

        /// <summary>
        /// 終點。
        /// </summary>
        public MenuVertex To { get; }

        /// <summary>
        /// 是否為自我迴圈（起點與終點是同一個頂點）。
        /// </summary>
        public bool IsLoop => From != null && ReferenceEquals(From, To);

        /// <summary>
        /// 是否為匯流邊：終點有多個前驅。依目前的圖即時計算。
        /// </summary>
        public bool IsConvergent => To != null && To.IsConvergent;

        /// <inheritdoc />
        public bool Equals(MenuEdge other) => ReferenceEquals(From, other.From) && ReferenceEquals(To, other.To);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is MenuEdge other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (RuntimeHelpers.GetHashCode(From) * 397) ^ RuntimeHelpers.GetHashCode(To);
            }
        }

        /// <summary>兩條邊的起點與終點都相同。</summary>
        public static bool operator ==(MenuEdge left, MenuEdge right) => left.Equals(right);

        /// <summary>兩條邊的起點或終點不同。</summary>
        public static bool operator !=(MenuEdge left, MenuEdge right) => !left.Equals(right);

        /// <summary>
        /// 回傳 <c>From -&gt; To</c>，只用於除錯與警告訊息。
        /// </summary>
        public override string ToString() => $"{From} -> {To}";
    }
}