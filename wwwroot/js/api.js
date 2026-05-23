// api.js — shared jQuery AJAX helpers

const BASE = '/RegistroDiCassa';

const Api = {
  request(method, url, data) {
    return $.ajax({
      method,
      url: BASE + url,
      contentType: 'application/json',
      data: data ? JSON.stringify(data) : undefined
    }).fail(xhr => {
      if (xhr.status === 401 && !window.location.pathname.includes('login.html')) {
        window.location.href = BASE + '/login.html';
      }
    });
  },
  get(url)        { return Api.request('GET', url); },
  post(url, data) { return Api.request('POST', url, data); },
  put(url, data)  { return Api.request('PUT', url, data); },
  del(url)        { return Api.request('DELETE', url); }
};

function formatEuro(n) {
  return '€ ' + parseFloat(n).toLocaleString('it-IT', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatDate(iso) {
  if (!iso) return '';
  return new Date(iso).toLocaleDateString('it-IT');
}

function formatDateTime(iso) {
  if (!iso) return '';
  return new Date(iso).toLocaleString('it-IT');
}

function showAlert(containerId, message, type = 'danger') {
  const html = `<div class="alert alert-${type} alert-dismissible fade show" role="alert">
    ${message}
    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
  </div>`;
  $('#' + containerId).html(html);
}

function getParam(name) {
  return new URLSearchParams(window.location.search).get(name);
}

function requireAuth(callback) {
  Api.get('/api/account/me')
    .done(user => {
      window.currentUser = user;
      if (user.role === 'Amministratore') {
        $('.admin-only').show();
      } else {
        $('.admin-only').hide();
      }
      $('#navUserName').text(user.fullName);
      updatePageTitle(user.sede);
      applyTheme(user.theme || 'light');
      if (callback) callback(user);
    })
    .fail(() => {
      window.location.href = BASE + '/login.html';
    });
}

function updatePageTitle(sede) {
  const base = 'Registro di Cassa';
  const title = sede ? base + ' — ' + sede : base;
  document.title = title;
  $('#navBrand').text(title);
}

function doLogout() {
  Api.post('/api/account/logout').always(() => {
    window.location.href = BASE + '/login.html';
  });
}

// ── Theme ──

function applyTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
  const icon = theme === 'dark' ? 'bi-sun' : 'bi-moon';
  $('#btnTheme').html(`<i class="bi ${icon}"></i>`);
}

function toggleTheme() {
  const current = document.documentElement.getAttribute('data-theme') || 'light';
  const next = current === 'dark' ? 'light' : 'dark';
  applyTheme(next);
  Api.post('/api/account/theme', { theme: next })
    .fail(() => console.warn('Could not save theme preference.'));
}
