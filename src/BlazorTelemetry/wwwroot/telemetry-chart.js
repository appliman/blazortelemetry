const instances = new Map();
let loader;

function ensureEcharts() {
    if (window.echarts) return Promise.resolve();
    if (loader) return loader;
    loader = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = '/_content/BlazorTelemetry/vendor/echarts.min.js';
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    });
    return loader;
}

export async function render(elementId, labels, values, color, unit) {
    await ensureEcharts();
    const element = document.getElementById(elementId);
    if (!element) return;
    let chart = instances.get(elementId);
    if (!chart) {
        chart = window.echarts.init(element, null, { renderer: 'canvas' });
        instances.set(elementId, chart);
        new ResizeObserver(() => chart.resize()).observe(element);
    }
    chart.setOption({
        animationDuration: 450,
        animationEasing: 'cubicOut',
        grid: { left: 16, right: 18, top: 18, bottom: 28, containLabel: true },
        tooltip: { trigger: 'axis', backgroundColor: '#151b22', borderColor: '#39434f', textStyle: { color: '#eef4f8' }, valueFormatter: value => `${value}${unit ? ` ${unit}` : ''}` },
        xAxis: { type: 'category', data: labels, boundaryGap: false, axisLine: { lineStyle: { color: '#39434f' } }, axisLabel: { color: '#8f9daa', hideOverlap: true }, axisTick: { show: false } },
        yAxis: { type: 'value', splitLine: { lineStyle: { color: '#242c35' } }, axisLabel: { color: '#8f9daa' } },
        series: [{ type: 'line', data: values, smooth: 0.28, showSymbol: false, lineStyle: { color, width: 2 }, areaStyle: { color: `${color}20` } }]
    }, true);
}

export function dispose(elementId) {
    const chart = instances.get(elementId);
    if (chart) chart.dispose();
    instances.delete(elementId);
}

export function focusById(elementId) {
    document.getElementById(elementId)?.focus({ preventScroll: true });
}
