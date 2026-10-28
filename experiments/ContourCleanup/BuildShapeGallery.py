"""Собирает автономную HTML-галерею экспериментов формы по пяти FIMOZ."""

import json
import shutil
from pathlib import Path


HERE = Path(__file__).resolve().parent
WORKTREE = HERE.parents[1]
ARTIFACTS = HERE / "artifacts"
OUTPUT = ARTIFACTS / "shape-review-gallery"
ASSETS = OUTPUT / "assets"
NAMES = ["Assaultman", "Bowman", "Pistolman", "Rifleman", "StrongHand"]

VARIANTS = [
    ("original", "Исходник", "Референс", "1254×1254 до обработки", "fimoz-user-samples/FIMOZ/{stem}.png"),
    ("old", "Старый 2→4", "Контроль", "Отдельный путь 1254→627→157; не вход градиента", "fimoz-user-samples/pipeline-smooth-2x4/{stem}-reduce4.png"),
    ("baseline", "База 8 px", "Контроль", "1254→157: подготовка 3×3, сетка 8, LAB 128", "shape-research/finish-cli/{stem}.png"),
    ("gradient-default", "Градиент · обычный", "FAIL 1/5", "Очиститель поверх базы 8 px: 3–16 изменённых клеток", "shape-gradient/{stem}-gradient-default.png"),
    ("gradient-opposition", "Градиент · строгий", "PASS 5/5", "Очиститель поверх базы 8 px; на 4/5 совпадает с ней", "shape-gradient/{stem}-gradient-opposition.png"),
    ("native-clean", "Очистка исходника", "Проба", "Очиститель до уменьшения, результат 1254×1254", "shape-research/native-clean-probe/{stem}-clean-native.png"),
    ("native-clean-opposition", "Исходник · строгий", "Проба", "Строгая очистка до уменьшения; 0 правок", "shape-research/native-clean-probe/{stem}-clean-native-opposition.png"),
    ("native-clean-before-finish", "Очистка → база", "Проба", "Итог 157×157 попиксельно равен базе", "shape-research/native-clean-probe/{stem}-clean-before-finish.png"),
    ("regions", "Цветовые области", "FAIL 1/5", "Выступы альфы у 4/5", "shape-regions/{stem}-regions.png"),
    ("regions-protected", "Области · защита", "PASS 5/5", "Меняет две цветные клетки Bowman", "shape-regions/{stem}-regions-protected.png"),
    ("graph", "Граф областей", "FAIL 1/5", "Выступы альфы у 4/5", "shape-region-graph/{stem}-graph.png"),
    ("graph-protected", "Граф · защита", "PASS 5/5", "На 5/5 совпадает с базой", "shape-region-graph/{stem}-graph-protected.png"),
    ("conservative", "Итерации · осторожно", "PASS 5/5", "Форма не стала доказуемо лучше", "shape-iterative-feedback/{stem}-conservative.png"),
    ("aggressive", "Итерации · смело", "PASS 5/5", "Сильнее меняет тон; форма не доказана", "shape-iterative-feedback/{stem}-aggressive.png"),
    ("agreement", "Карта согласия", "Диагностика", "Расхождения четырёх масштабов", "shape-iterative-feedback/{stem}-agreement.png"),
]

BRANCHES = {
    "shape-gradient": WORKTREE.parent / "shape-gradient",
    "shape-regions": WORKTREE.parent / "shape-regions",
    "shape-region-graph": WORKTREE.parent / "shape-region-graph",
    "shape-iterative-feedback": WORKTREE.parent / "shape-iterative-feedback",
}

VALIDATION_CATEGORIES = {
    "baseline-8x": "Контрольные увеличения",
    "checker-8x": "Прозрачность на шахматном фоне",
    "gradient": "Градиент: отмеченные изменения",
    "regions": "Области: чёрный и белый фон",
    "graph": "Граф: чёрный и белый фон",
    "iterative": "Итерации: чёрный и белый фон",
    "edge-crops": "Дефекты альфа-контура",
    "iterative-crops": "Детали итераций",
    "protected-addendum": "Защищённый Bowman",
}


def copy_asset(source: Path, destination: str) -> str:
    if not source.is_file():
        raise FileNotFoundError(source)
    target = ASSETS / destination
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    return "assets/" + destination.replace("\\", "/")


def variant_source(relative: str) -> Path:
    folder, rest = relative.split("/", 1)
    if folder in BRANCHES:
        return BRANCHES[folder] / "experiments/ContourCleanup/artifacts" / relative
    return ARTIFACTS / relative


def build_manifest() -> dict:
    manifest = {"names": NAMES, "variants": [], "images": {}, "sheets": [], "details": []}

    for key, label, verdict, description, pattern in VARIANTS:
        manifest["variants"].append({"id": key, "label": label, "verdict": verdict, "description": description})

    for name in NAMES:
        stem = f"T_{name}_UNP"
        manifest["images"][name] = {}
        for key, _, _, _, pattern in VARIANTS:
            source = variant_source(pattern.format(stem=stem))
            manifest["images"][name][key] = copy_asset(source, f"variants/{name}/{key}.png")

    review = ARTIFACTS / "shape-research/review"
    for source in sorted(review.glob("*.png")):
        person = next((name for name in NAMES if name in source.stem), None)
        manifest["sheets"].append({
            "name": source.stem.replace("T_", "").replace("_UNP", "").replace("-", " · "),
            "person": person,
            "path": copy_asset(source, f"sheets/{source.name}"),
        })

    for branch_name, root in BRANCHES.items():
        source = root / "experiments/ContourCleanup/artifacts" / branch_name / "comparison.png"
        if source.is_file():
            manifest["sheets"].append({
                "name": branch_name.replace("shape-", "").replace("-", " · "),
                "person": None,
                "path": copy_asset(source, f"sheets/{branch_name}-comparison.png"),
            })

    validation = ARTIFACTS / "independent-validation"
    for folder, category in VALIDATION_CATEGORIES.items():
        for source in sorted((validation / folder).glob("*.png")):
            person = next((name for name in NAMES if source.name.startswith(name + "-")), None)
            manifest["details"].append({
                "name": source.stem.replace("-", " · "),
                "category": category,
                "person": person,
                "path": copy_asset(source, f"details/{folder}/{source.name}"),
            })

    return manifest


HTML = r'''<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>FIMOZ · Атлас формы</title>
<style>
:root{color-scheme:dark;--ink:#e9e8e2;--muted:#a7a9a4;--edge:#343a3d;--panel:#1c2224;--panel2:#242c2e;--accent:#b7e381;--bad:#f09b86;--good:#a8ddb3;--image-width:628px}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:#111719;color:var(--ink);font:14px/1.45 system-ui,Segoe UI,sans-serif}
button,select,input{font:inherit}button,select{color:var(--ink);background:var(--panel2);border:1px solid var(--edge);border-radius:9px;padding:8px 12px;cursor:pointer}button:hover,select:hover{border-color:#73887d}button.active{background:#36504a;border-color:var(--accent);color:#f7fff0}a{color:var(--accent)}
.wrap{max-width:1580px;margin:auto;padding:24px}.head{display:flex;justify-content:space-between;gap:20px;align-items:end}.eyebrow{font-size:11px;letter-spacing:.18em;color:var(--accent);font-weight:700}h1{font-size:clamp(28px,4vw,48px);line-height:1.06;margin:8px 0 10px;letter-spacing:-.04em}h2{font-size:20px;margin:0 0 12px}p{margin:0;color:var(--muted)}.head p{max-width:710px}.note{max-width:390px;padding:14px 16px;border:1px solid #45544b;border-radius:12px;background:#233129;color:#c9ddca}.note strong{color:#eaffdd}
.bar{position:sticky;top:0;z-index:10;background:#111719ed;backdrop-filter:blur(10px);border-bottom:1px solid var(--edge);margin:20px -24px 0;padding:10px 24px}.row{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.tabs{margin-top:10px}.view{display:none;padding-top:20px}.view.active{display:block}.toolbar{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:16px}.toolbar label{color:var(--muted)}.toolbar input{accent-color:var(--accent)}.spacer{flex:1}
.compare{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.card{border:1px solid var(--edge);background:var(--panel);border-radius:14px;overflow:hidden;min-width:0}.cardhead{display:flex;justify-content:space-between;align-items:center;gap:10px;padding:12px 14px;border-bottom:1px solid var(--edge)}.cardhead b{font-size:15px}.cardhead select{max-width:100%;width:100%}.caption{padding:9px 14px;color:var(--muted);font-size:12px;min-height:38px}.stage{height:510px;overflow:auto;background-color:#232b2c;background-image:linear-gradient(45deg,#35403f 25%,transparent 25%),linear-gradient(-45deg,#35403f 25%,transparent 25%),linear-gradient(45deg,transparent 75%,#35403f 75%),linear-gradient(-45deg,transparent 75%,#35403f 75%);background-size:24px 24px;background-position:0 0,0 12px,12px -12px,-12px 0;cursor:zoom-in}.stage.white,.tileimage.white{background:#fff}.stage.black,.tileimage.black{background:#080808}.stage img{display:block;width:var(--image-width);height:var(--image-width);max-width:none;margin:auto;object-fit:contain;image-rendering:pixelated}.stage img.original{image-rendering:auto}.cardfoot{display:flex;justify-content:space-between;padding:9px 14px;color:var(--muted);border-top:1px solid var(--edge);font-size:12px}.cardfoot a{white-space:nowrap}
.matrix{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:12px}.tile{border:1px solid var(--edge);border-radius:12px;background:var(--panel);overflow:hidden}.tileimage{height:225px;display:grid;place-items:center;background-color:#26302f;background-image:linear-gradient(45deg,#394340 25%,transparent 25%),linear-gradient(-45deg,#394340 25%,transparent 25%),linear-gradient(45deg,transparent 75%,#394340 75%),linear-gradient(-45deg,transparent 75%,#394340 75%);background-size:20px 20px;background-position:0 0,0 10px,10px -10px,-10px 0;cursor:zoom-in}.tileimage img{max-width:100%;max-height:100%;object-fit:contain;image-rendering:pixelated}.tileimage img.original{image-rendering:auto}.tilebody{padding:11px 12px}.tilebody b{display:block}.tilebody small{color:var(--muted)}.badge{font-size:10px;font-weight:750;letter-spacing:.04em;border:1px solid #526457;border-radius:20px;padding:4px 7px;white-space:nowrap;color:var(--good)}.badge.fail{border-color:#714b43;color:var(--bad)}.badge.neutral{border-color:#566066;color:#bac4c6}.sectionline{display:flex;justify-content:space-between;gap:12px;align-items:center;margin:18px 0 12px}.sectionline p{max-width:730px}.sheetgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:12px}.sheetgrid .tileimage{height:270px;background:#151b1c}.sheetgrid img{image-rendering:auto}
.empty{padding:30px;color:var(--muted);border:1px dashed var(--edge);border-radius:12px}.lightbox{position:fixed;inset:0;z-index:30;background:#080c0deF;display:none;flex-direction:column}.lightbox.open{display:flex}.lighttop{display:flex;gap:10px;align-items:center;justify-content:space-between;padding:12px 18px;background:#1a2223}.lightbody{flex:1;overflow:auto;display:grid;place-items:center;padding:20px}.lightbody img{max-width:100%;max-height:calc(100vh - 100px);object-fit:contain;image-rendering:pixelated}.lightbody img.sheet{image-rendering:auto}.hint{font-size:12px;color:var(--muted)}
@media(max-width:950px){.head{display:block}.note{max-width:none;margin-top:16px}.compare{grid-template-columns:1fr}.stage{height:430px}.sheetgrid{grid-template-columns:1fr}}
</style>
</head>
<body>
<div class="wrap">
  <header class="head"><div><div class="eyebrow">TESSERA / FIMOZ / 27.10.2026</div><h1>Атлас формы</h1><p>Пять исходников, базовый результат, четыре гипотезы и проверочные снимки. Выберите персонажа и листайте варианты. Нажмите на картинку для просмотра крупнее.</p></div><div class="note"><strong>Как читать PASS:</strong> вариант прошёл технический порог на этих пяти файлах. Это не подтверждение улучшения формы и не художественная приёмка.</div></header>
  <div class="bar"><div class="row" id="people"></div><div class="row tabs" id="tabs"></div></div>
  <section id="compare" class="view active"><div class="toolbar"><label>Увеличение</label><input id="zoom" type="range" min="2" max="8" value="4"><span id="zoomText">4×</span><span class="spacer"></span><label>Фон</label><select id="background"><option value="checker">Шахматный</option><option value="white">Белый</option><option value="black">Чёрный</option></select></div><p style="margin-bottom:14px;color:#c9ddca">Чтобы оценить вклад очистителя, сравнивайте «База 8 px» с «Градиент · обычный». «Старый 2→4» — отдельная предварительная цепочка; градиент к ней не применялся.</p><div class="compare" id="compareCards"></div><p style="margin-top:14px">Клавиши ← и → переключают персонажей. В выпадающих списках можно сопоставить любые два варианта.</p></section>
  <section id="matrix" class="view"><div class="sectionline"><div><h2>Все варианты одного исходника</h2><p>Исходник, старый контроль, базовый результат и все выходы экспериментальных веток.</p></div></div><div class="matrix" id="matrixCards"></div></section>
  <section id="sheets" class="view"><div class="sectionline"><div><h2>Обзорные листы</h2><p>Общие сравнения и листы для выбранного персонажа. Переключите персонажа выше, чтобы листать его материалы.</p></div></div><div class="sheetgrid" id="sheetCards"></div></section>
  <section id="details" class="view"><div class="sectionline"><div><h2>Проверочные снимки валидатора</h2><p>Увеличения, фоны и отмеченные дефекты. Здесь сохранены изображения из независимой проверки, а не только успешные варианты.</p></div></div><div class="toolbar"><label>Категория</label><select id="category"></select></div><div class="sheetgrid" id="detailCards"></div></section>
</div>
<div class="lightbox" id="lightbox" role="dialog" aria-modal="true" aria-label="Просмотр изображения"><div class="lighttop"><b id="lightTitle"></b><span class="hint">Esc — закрыть</span><button id="closeLight">Закрыть ×</button></div><div class="lightbody" id="lightBody"></div></div>
<script type="application/json" id="manifest">__MANIFEST__</script>
<script>
const data=JSON.parse(document.getElementById('manifest').textContent);
const state={person:data.names[0],tab:'compare',left:'baseline',right:'gradient-default',background:'checker',zoom:4,category:'Все'};
const $=id=>document.getElementById(id);
const variant=id=>data.variants.find(x=>x.id===id);
const badge=v=>{const kind=v.verdict.includes('FAIL')?'fail':v.verdict.includes('PASS')?'':'neutral';return `<span class="badge ${kind}">${v.verdict}</span>`};
function renderPeople(){ $('people').innerHTML=data.names.map(n=>`<button type="button" class="${n===state.person?'active':''}" data-person="${n}">${n}</button>`).join(''); document.querySelectorAll('[data-person]').forEach(b=>b.onclick=()=>{state.person=b.dataset.person;render()}) }
function renderTabs(){const tabs=[['compare','Сравнение'],['matrix','Матрица'],['sheets','Обзорные листы'],['details','Детали проверки']];$('tabs').innerHTML=tabs.map(([id,label])=>`<button type="button" class="${id===state.tab?'active':''}" data-tab="${id}">${label}</button>`).join('');document.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>{state.tab=b.dataset.tab;render()});document.querySelectorAll('.view').forEach(el=>el.classList.toggle('active',el.id===state.tab))}
function optionList(selected){return data.variants.map(v=>`<option value="${v.id}" ${v.id===selected?'selected':''}>${v.label}</option>`).join('')}
function compareCard(title,id,selector){const v=variant(id),src=data.images[state.person][id];return `<article class="card"><div class="cardhead"><div style="min-width:0;flex:1"><small style="color:var(--muted)">${title}</small><select aria-label="${title}" data-variant="${selector}">${optionList(id)}</select></div>${badge(v)}</div><div class="stage ${state.background}" data-open="${src}" data-title="${state.person} · ${v.label}"><img class="${id==='original'||id.startsWith('native-clean')&&id!=='native-clean-before-finish'?'original':''}" src="${src}" alt="${state.person}: ${v.label}"></div><div class="caption">${v.description}</div><div class="cardfoot"><span>${v.label}</span><a href="${src}" target="_blank">Открыть PNG ↗</a></div></article>`}
function renderCompare(){const orig=data.images[state.person].original;$('compareCards').innerHTML=`<article class="card"><div class="cardhead"><b>Исходник</b><span class="badge neutral">Референс</span></div><div class="stage ${state.background}" data-open="${orig}" data-title="${state.person} · исходник"><img class="original" src="${orig}" alt="Исходник ${state.person}"></div><div class="caption">Оригинал 1254×1254; для оценки формы после обработки.</div><div class="cardfoot"><span>До преобразования</span><a href="${orig}" target="_blank">Открыть PNG ↗</a></div></article>`+compareCard('Вариант A',state.left,'left')+compareCard('Вариант B',state.right,'right');document.querySelectorAll('[data-variant]').forEach(el=>el.onchange=()=>{state[el.dataset.variant]=el.value;renderCompare()});attachOpen()}
function tile(src,label,sub,id){return `<article class="tile"><div class="tileimage ${state.background}" data-open="${src}" data-title="${label}"><img class="${id==='original'?'original':''}" loading="lazy" src="${src}" alt="${label}"></div><div class="tilebody"><b>${label}</b><small>${sub}</small><div style="margin-top:5px"><a href="${src}" target="_blank">Открыть PNG ↗</a></div></div></article>`}
function renderMatrix(){ $('matrixCards').innerHTML=data.variants.map(v=>tile(data.images[state.person][v.id],v.label,v.verdict+' · '+v.description,v.id==='native-clean'||v.id==='native-clean-opposition'?'original':v.id)).join('');attachOpen() }
function renderSheets(){const items=data.sheets.filter(x=>!x.person||x.person===state.person);$('sheetCards').innerHTML=items.map(x=>tile(x.path,x.name,x.person||'Все пять исходников','sheet')).join('')||'<div class="empty">Листов нет.</div>';attachOpen()}
function renderDetails(){const categories=['Все',...new Set(data.details.map(x=>x.category))];$('category').innerHTML=categories.map(c=>`<option ${c===state.category?'selected':''}>${c}</option>`).join('');$('category').onchange=e=>{state.category=e.target.value;renderDetails()};const items=data.details.filter(x=>(!x.person||x.person===state.person)&&(state.category==='Все'||x.category===state.category));$('detailCards').innerHTML=items.map(x=>tile(x.path,x.name,x.category,'sheet')).join('')||'<div class="empty">Снимков в этой категории нет.</div>';attachOpen()}
function attachOpen(){document.querySelectorAll('[data-open]').forEach(el=>el.onclick=()=>openLight(el.dataset.open,el.dataset.title))}
function openLight(src,title){$('lightTitle').textContent=title;$('lightBody').innerHTML='';const img=document.createElement('img');img.src=src;img.alt=title;if(src.includes('/sheets/')||src.includes('/details/'))img.className='sheet';$('lightBody').append(img);$('lightbox').classList.add('open')}
function closeLight(){$('lightbox').classList.remove('open')}
function render(){renderPeople();renderTabs();renderCompare();renderMatrix();renderSheets();renderDetails()}
$('zoom').oninput=e=>{state.zoom=Number(e.target.value);document.documentElement.style.setProperty('--image-width',(state.zoom*157)+'px');$('zoomText').textContent=state.zoom+'×'};
$('background').onchange=e=>{state.background=e.target.value;renderCompare();renderMatrix();renderSheets();renderDetails()};
$('closeLight').onclick=closeLight;$('lightbox').onclick=e=>{if(e.target.id==='lightbox'||e.target.id==='lightBody')closeLight()};
document.onkeydown=e=>{if(e.key==='Escape')closeLight();if($('lightbox').classList.contains('open'))return;if(e.key==='ArrowRight'||e.key==='ArrowLeft'){const i=data.names.indexOf(state.person),d=e.key==='ArrowRight'?1:-1;state.person=data.names[(i+d+data.names.length)%data.names.length];render()}};
render();
</script>
</body>
</html>'''


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    manifest = build_manifest()
    embedded = json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).replace("<", "\\u003c")
    (OUTPUT / "index.html").write_text(HTML.replace("__MANIFEST__", embedded), encoding="utf-8")
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    shutil.copy2(HERE / "FIMOZ_GALLERY_METHODOLOGY.md", OUTPUT / "METHODOLOGY.md")
    count = sum(1 for _ in ASSETS.rglob("*.png"))
    print(f"Gallery: {OUTPUT / 'index.html'}")
    print(f"Images: {count}")


if __name__ == "__main__":
    main()
