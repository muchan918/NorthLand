namespace NorthLand.Combat
{
    /// <summary>
    /// 정보 패널 스탯 행 한 줄(#536). **액션이 만들고 <c>TowerInfoUI</c>가 그린다.**
    /// 정보 패널에 뜨는 줄은 예외 없이 이 타입을 거친다 — 예전의 문자열 경로
    /// (<c>TowerAction.DescribeStats</c> → <c>Tower.BuildStatsText</c>)는 제거됐다.
    ///
    /// <para><b>왜 float 쌍이 아니라 표시 문자열 3칸인가</b>: 행에는 성격이 다른 두 종류가 들어온다 —
    /// 원장 4축(<see cref="TowerStat"/>)처럼 기본값과 실제값이 있는 줄과, 연발·감속·지속피해처럼
    /// 값이 하나뿐인 줄이다. 둘을 같은 타입으로 담으려면 "숫자"가 아니라 "칸에 들어갈 문자열"이
    /// 공통 분모다. 서식과 라벨 조회는 <see cref="TowerStatsFormatter"/>가 계속 단독으로 소유한다.</para>
    /// </summary>
    public readonly struct TowerStatRowData
    {
        /// 왼쪽 칸(Stat). 이미 해석된 표시 문자열이다 — 로컬라이즈 키가 아니다.
        public readonly string Label;

        /// 가운데 칸(OriginStat). 값이 하나뿐인 줄은 이 칸만 쓴다.
        public readonly string BaseText;

        /// 오른쪽 칸(BuffedStat). <b>null이면 화살표와 이 칸을 끈다</b> —
        /// 버프가 없는 축과 값이 하나뿐인 줄이 같은 모양이 된다.
        public readonly string BuffedText;

        TowerStatRowData(string label, string baseText, string buffedText)
        {
            Label = label;
            BaseText = baseText;
            BuffedText = buffedText;
        }

        /// <summary>
        /// 원장 축 한 줄. 기본값과 실제값이 <b>서식 후 문자열로</b> 다를 때만 버프 칸을 채운다.
        /// <para>값으로 비교하지 않는 이유: 표시 자리에서 반올림돼 사라질 차이(18.00 → 18.02가
        /// <c>0.#</c>에서 둘 다 "18")까지 버프로 잡혀 <c>18 → 18</c>이 뜬다. 플레이어가 보는 것이
        /// 문자열이므로 판단도 문자열에서 한다.</para>
        /// </summary>
        public static TowerStatRowData Stat(string labelKey, float baseValue, float finalValue,
                                            string format = TowerStatsFormatter.k_DefaultFormat)
            => StatLabeled(TowerStatsFormatter.StatLabel(labelKey), baseValue, finalValue, format);

        /// <summary>
        /// 라벨을 **이미 해석해서** 넘기는 <see cref="Stat"/>. 라벨이 키 하나로 떨어지지 않는 행
        /// (예: 빔의 `DPS ×3` — 동시 타격 대상 수가 라벨에 붙는다)이 쓴다.
        /// <para>대상 수를 값 칸이 아니라 라벨에 붙이는 이유: 값 칸은 <b>서식 후 문자열 비교</b>로
        /// 화살표를 가르므로, 거기에 배수 접미사가 남으면 기본값과 실제값이 영원히 달라 보인다.</para>
        /// </summary>
        public static TowerStatRowData StatLabeled(string label, float baseValue, float finalValue,
                                                   string format = TowerStatsFormatter.k_DefaultFormat)
        {
            string baseText = baseValue.ToString(format);
            string finalText = finalValue.ToString(format);

            return new TowerStatRowData(label, baseText, baseText == finalText ? null : finalText);
        }

        /// <summary>
        /// 값이 하나뿐인 줄(연발·감속·지속피해·성장 등). 원장 축이 아니라 "이 타워가 무엇을 하는가"의
        /// 서술이라 기본값/실제값 구분이 없다 — 넘어오는 값은 이미 실효값이다.
        /// <para><paramref name="label"/>은 <b>이미 해석된 문자열</b>이다 — 호출부가
        /// <see cref="TowerStatsFormatter.StatLabel"/>이나 <see cref="TowerStatsFormatter.EffectName"/>으로
        /// 조회해서 넘긴다. 여기서 키를 받지 않는 이유는 `DPS ×3`처럼 라벨이 키 하나로 떨어지지 않는
        /// 행이 있기 때문이다. <b>새 행을 추가할 때 하드코딩 문자열을 넣지 말 것</b> — 이 줄들도 전부
        /// 로컬라이즈 키를 갖는다(#536).</para>
        /// </summary>
        public static TowerStatRowData Note(string label, string value)
            => new TowerStatRowData(label, value, null);

        /// 버프 칸을 그릴지 여부. 뷰가 화살표와 오른쪽 칸을 함께 가르는 기준이다.
        public bool HasBuffedValue => BuffedText != null;
    }
}
