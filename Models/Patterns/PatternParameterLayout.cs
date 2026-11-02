namespace StockDashboard.Models.Patterns
{
    /// <summary>
    /// 參數面板的排版單位：單一參數，或「勾選參數 + 它控制的數值參數」組成的一組 (畫成一個框)。
    /// </summary>
    public class PatternParameterGroup
    {
        public PatternParameter Head { get; init; } = default!;

        // 由 Head 勾選控制的參數 (EnabledBy == Head.Key)；單一參數時為空
        public List<PatternParameter> Children { get; init; } = new();

        public bool IsGroup => Children.Count > 0;
    }

    public static class PatternParameterLayout
    {
        /// <summary>
        /// 依 EnabledBy 把參數分組：有附屬參數的勾選參數與其附屬參數成為一組。
        /// 單一參數排在前面、群組排在後面 (各自維持原定義順序)，讓群組框並排、版面整齊。
        /// </summary>
        public static List<PatternParameterGroup> Build(IReadOnlyList<PatternParameter> parameters)
        {
            var items = parameters
                .Where(p => p.EnabledBy == null)
                .Select(p => new PatternParameterGroup
                {
                    Head = p,
                    Children = parameters.Where(c => c.EnabledBy == p.Key).ToList()
                })
                .ToList();

            return items.Where(i => !i.IsGroup).Concat(items.Where(i => i.IsGroup)).ToList();
        }
    }
}
