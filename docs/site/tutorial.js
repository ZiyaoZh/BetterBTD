'use strict';

const search = document.querySelector('#search');
const results = document.querySelector('#search-results');
const status = document.querySelector('#search-status');
const sections = [...document.querySelectorAll('main section')];
const entries = [...document.querySelectorAll('main section, main details, main h3[id]')].map(element => ({
  id: element.id,
  title: (element.querySelector('h2, h1, summary') || element).textContent.trim(),
  text: element.textContent.toLocaleLowerCase()
}));

search.addEventListener('input', () => {
  const query = search.value.trim().toLocaleLowerCase();
  results.replaceChildren();
  if (!query) {
    results.hidden = true;
    status.textContent = '';
    return;
  }
  const matches = entries.filter(entry => entry.text.includes(query));
  results.hidden = false;
  status.textContent = matches.length ? `找到 ${matches.length} 个相关章节` : '没有找到，请尝试“金币”“升级”或“绑定”。';
  for (const entry of matches) {
    const item = document.createElement('li');
    const link = document.createElement('a');
    link.href = `#${entry.id}`;
    link.textContent = entry.title;
    item.append(link);
    results.append(item);
  }
});

function revealTarget() {
  const target = document.getElementById(location.hash.slice(1));
  if (!target) return;
  const detail = target.closest('details');
  if (detail) {
    detail.open = true;
    requestAnimationFrame(() => target.scrollIntoView());
  }
}
window.addEventListener('hashchange', revealTarget);
revealTarget();

if ('IntersectionObserver' in window) {
  const visible = new Set();
  const observer = new IntersectionObserver(changes => {
    changes.forEach(change => change.isIntersecting ? visible.add(change.target) : visible.delete(change.target));
    const current = sections.find(section => visible.has(section));
    if (!current) return;
    document.querySelectorAll('#toc a').forEach(link => {
      if (link.hash === `#${current.id}`) link.setAttribute('aria-current', 'location');
      else link.removeAttribute('aria-current');
    });
  }, {rootMargin: '-80px 0px -45% 0px'});
  sections.forEach(section => observer.observe(section));
}

// Expand instruction explanations for printing, then restore the reading state.
let printState = [];
window.addEventListener('beforeprint', () => {
  printState = [...document.querySelectorAll('details')].map(detail => [detail, detail.open]);
  printState.forEach(([detail]) => { detail.open = true; });
});
window.addEventListener('afterprint', () => {
  printState.forEach(([detail, wasOpen]) => { detail.open = wasOpen; });
});
