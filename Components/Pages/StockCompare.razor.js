// StockCompare.razor.js

// 使用者設定的預設股票代號：存在瀏覽器 localStorage (只在這台電腦的這個瀏覽器有效；讀寫失敗時安靜略過)
export function loadDefaultIds(key) {
    try { return localStorage.getItem(key) || ''; } catch (e) { return ''; }
}

export function saveDefaultIds(key, value) {
    try { localStorage.setItem(key, value); return true; } catch (e) { return false; }
}

export function renderCompareChart(containerId, chartData) {
    const chartDom = document.getElementById(containerId);
    if (!chartDom) return;

    // 重複點擊「開始比較」時沿用同一個圖表實例，避免重複 init 造成 ECharts 警告與記憶體浪費
    const myChart = echarts.getInstanceByDom(chartDom) || echarts.init(chartDom);

    // 準備 ECharts 的 series 陣列
    // C# 傳來的結構：chartData.timeline (X軸), chartData.seriesData (各股數組)
    const seriesConfig = chartData.seriesData.map(item => {
        return {
            name: item.name,
            type: 'line',
            data: item.data,
            smooth: false,
            symbol: 'circle',
            symbolSize: 4,
            showSymbol: false,
            lineStyle: { width: 1 }
        };
    });

    const option = {
        title: {
            text: '個股累計績效走勢對比',
            textStyle: { color: '#ffffff', fontSize: 14 }
        },
        tooltip: {
            trigger: 'axis',
            backgroundColor: 'rgba(30, 34, 45, 0.9)',
            borderColor: '#434651',
            borderWidth: 1,
            textStyle: { color: '#d1d4dc' },
            axisPointer: { type: 'cross' },
            // 格式化 Tooltip，滑鼠游標指上去時顯示各檔股票當天的%數
            formatter: function (params) {
                let html = `<div style="font-weight:bold;color:#fff;margin-bottom:5px;">日期：${params[0].name}</div>`;
                params.forEach(p => {
                    // 根據正負報酬率給予不同顏色
                    const color = p.value >= 0 ? '#ef5350' : '#26a69a';
                    html += `
                        <div style="display:flex; justify-content:space-between; gap:20px; font-size:13px;">
                            <span>🟢 ${p.seriesName}:</span>
                            <span style="color:${color}; font-weight:bold;">${p.value > 0 ? '+' : ''}${p.value}%</span>
                        </div>`;
                });
                return html;
            }
        },
        legend: {
            data: chartData.seriesData.map(item => item.name),
            textStyle: { color: '#8392A5' },
            top: '2%'
        },
        grid: {
            left: '5%',
            right: '5%',
            bottom: '12%',
            top: '15%',
            containLabel: true
        },
        xAxis: {
            type: 'category',
            data: chartData.timeline,
            axisLine: { lineStyle: { color: '#8392A5' } },
            splitLine: { show: false }
        },
        yAxis: {
            type: 'value',
            axisLabel: {
                formatter: '{value}%' // Y 軸刻度加上百分比符號
            },
            axisLine: { lineStyle: { color: '#8392A5' } },
            splitLine: { show: true, lineStyle: { color: '#2d3139' } }
        },
        dataZoom: [
            { type: 'inside', start: 0, end: 100 },
            {
                type: 'slider',
                show: true,
                bottom: '2%',
                textStyle: { color: '#8392A5' }
            }
        ],
        series: seriesConfig
    };

    myChart.setOption(option, true); // true 代表完全覆蓋舊圖表，切換天數時不會殘留舊線
}