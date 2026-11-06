// StockChart.razor.js：技術圖表 = K 線 + 均線 + 附圖 (成交量 / MACD / KD / RSI / 三大法人...) 依序疊在下方
// 附圖定義與組圖程式在 wwwroot/js/candle-chart.js (與型態掃描小卡共用)

// 版本參數沿用本檔網址的 ?v=，讓共用檔也跟著更新 (避免瀏覽器沿用快取中的舊版)
const { UP, DOWN, MA_LINES, SUB_CHARTS, buildStackedOption, stackedHeight, stackedBackground, infoTexts, infoRich } = await import('../../js/candle-chart.js' + new URL(import.meta.url).search);

// 版面 (像素)：K 線區高度固定，每多一個附圖，圖表就增高 subLegend + subHeight (費半列加高，見 candle-chart.js 的 heightScale)
const LAYOUT = {
    top: 60,           // 標題與均線圖例
    candleHeight: 340,
    subLegend: 18,
    subHeight: 70,
    bottom: 80,        // 日期標籤 + 縮放滑桿
    left: 60,
    right: 40
};
const VISIBLE_BARS = 60;                 // 預設縮放：顯示最近約 60 根 (不論載入半年或全部)
const FOCUS_BEFORE = 30, FOCUS_AFTER = 60;   // 有標示日期時：顯示該日前 30 根、後 60 根
const FOCUS_COLOR = '#ffd54f';           // 標示日期 (同型態掃描小卡的型態成立日)

// 預設縮放範圍 (百分比)
function defaultZoom(candles, focusDate) {
    const n = candles.length;
    if (n === 0) return { start: 0, end: 100 };
    const focus = focusDate ? candles.findIndex(c => c.date >= focusDate) : -1;
    if (focus >= 0) {
        const from = Math.max(0, focus - FOCUS_BEFORE), to = Math.min(n - 1, focus + FOCUS_AFTER);
        return { start: from / n * 100, end: (to + 1) / n * 100 };
    }
    return { start: Math.max(0, 100 - VISIBLE_BARS / n * 100), end: 100 };
}

// 圖表寬度跟著容器 (視窗大小改變、側邊欄收合) 自動調整
function bindResize(dom, chart) {
    if (dom.__resizeObserver) return;
    dom.__resizeObserver = new ResizeObserver(() => chart.resize());
    dom.__resizeObserver.observe(dom);
}

// candles：PatternCandle 陣列 (camelCase)；visibleMas：預設顯示的均線名稱；subCharts：附圖 (依序)；settings：指標參數
// focusDate：要標示的日期 (從型態掃描小卡開啟時)，沒有則為 null
export function renderStockChart(containerId, stockLabel, candles, visibleMas, subCharts, settings, focusDate) {
    const dom = document.getElementById(containerId);
    if (!dom) return;
    const subs = (subCharts || []).filter(s => SUB_CHARTS[s]);
    dom.style.height = stackedHeight(LAYOUT, subs) + 'px';
    dom.style.backgroundImage = stackedBackground(LAYOUT, subs);   // 各附圖列的淡底色

    // ECharts 由 App.razor 的 <script> 全域載入；重繪時沿用同一個圖表實例，並保留使用者目前的縮放範圍
    // 換股票、換顯示筆數或標示日期時，縮放範圍回到預設；只換附圖 / 參數時保留目前縮放
    const viewKey = `${stockLabel}|${candles.length}|${candles[0]?.date}|${focusDate ?? ''}`;
    let chart = echarts.getInstanceByDom(dom);
    let zoom = defaultZoom(candles, focusDate);
    if (chart) {
        const current = chart.getOption()?.dataZoom?.[0];
        if (current && chart.__viewKey === viewKey) zoom = { start: current.start, end: current.end };
        chart.resize();
    } else {
        chart = echarts.init(dom);
    }
    chart.__viewKey = viewKey;
    bindResize(dom, chart);

    const option = buildStackedOption({
        candles, subs, settings: settings || {}, layout: LAYOUT,
        mainSeries: {
            type: 'candlestick',
            data: candles.map(c => [c.open, c.close, c.low, c.high]),
            itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN },
            markLine: focusDate && candles.some(c => c.date === focusDate) ? {
                silent: true, symbol: 'none', lineStyle: { color: FOCUS_COLOR, type: 'dashed', width: 1 },
                label: { show: true, formatter: '型態成立', position: 'end', color: FOCUS_COLOR, fontSize: 10 },
                data: [{ xAxis: focusDate }]
            } : undefined
        },
        tooltipTitle: c => `日期：${c.date}${c.date === focusDate ? '（型態成立）' : ''}`,
        dateFormatter: d => d,
        maLegend: { top: 32, right: LAYOUT.right },
        headerInfo: true
    });
    option.title = { text: `${stockLabel} 技術分析圖`, textStyle: { color: '#fff' } };
    option.legend[0].selected = Object.fromEntries(MA_LINES.map(m => [m.name, (visibleMas || []).includes(m.name)]));
    // 縮放與拖曳：滑鼠滾輪 / 拖曳 (inside) 與下方滑桿 (slider)，所有附圖一起連動
    option.dataZoom = [
        { type: 'inside', xAxisIndex: 'all', ...zoom },
        {
            type: 'slider', xAxisIndex: 'all', ...zoom, bottom: 12, height: 24,
            borderColor: '#2d3139', textStyle: { color: '#8392A5' }, handleStyle: { color: '#8392A5' }
        }
    ];
    // 當天數值顯示在各圖上方：預設最新一天（或標示日），滑鼠移動時跟著十字線，移開後回到預設
    const defaultIndex = Math.max(0, focusDate ? candles.findIndex(c => c.date === focusDate) : candles.length - 1);
    chart.__info = { candles, subs, settings: settings || {}, visibleMas: visibleMas || [], defaultIndex };
    const rich = infoRich();
    const showInfo = index => {
        const i = chart.__info;
        const texts = infoTexts(i.candles, i.subs, i.settings, i.visibleMas, index);
        chart.setOption({ graphic: Object.entries(texts).map(([id, text]) => ({ id, type: 'text', style: { text, rich } })) });
    };
    chart.setOption(option, true);
    showInfo(defaultIndex);
    chart.off('updateAxisPointer');
    chart.off('globalout');
    chart.on('updateAxisPointer', e => {
        const v = e.axesInfo?.[0]?.value;
        const idx = typeof v === 'number' ? v : candles.findIndex(c => c.date === v);
        if (idx >= 0 && idx < candles.length) showInfo(idx);
    });
    chart.on('globalout', () => showInfo(chart.__info.defaultIndex));
}

export function disposeChart(containerId) {
    const dom = document.getElementById(containerId);
    if (dom?.__resizeObserver) { dom.__resizeObserver.disconnect(); dom.__resizeObserver = null; }
    const chart = dom && echarts.getInstanceByDom(dom);
    if (chart) chart.dispose();
}
