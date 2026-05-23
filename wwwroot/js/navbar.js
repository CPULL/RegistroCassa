// navbar.js — injects the shared navbar

const SEDI = ['PontiRossi', 'Poggio', 'Porcellane'];

function renderNavbar() {
  const html = `
  <nav class="navbar navbar-expand-md navbar-dark mb-3">
    <div class="container-fluid">
      <a class="navbar-brand" id="navBrand" href="${BASE}/index.html">Registro di Cassa</a>
      <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#navMenu">
        <span class="navbar-toggler-icon"></span>
      </button>
      <div class="collapse navbar-collapse" id="navMenu">
        <ul class="navbar-nav me-auto">
          <li class="nav-item">
            <a class="nav-link" href="${BASE}/index.html">
              <i class="bi bi-calendar-day me-1"></i>Giornata
            </a>
          </li>
          <li class="nav-item admin-only" style="display:none">
            <a class="nav-link" href="${BASE}/report.html">
              <i class="bi bi-file-earmark-excel me-1"></i>Report
            </a>
          </li>
          <li class="nav-item admin-only" style="display:none">
            <a class="nav-link" href="${BASE}/utenti.html">
              <i class="bi bi-people me-1"></i>Utenti
            </a>
          </li>
        </ul>
        <ul class="navbar-nav ms-auto align-items-center gap-2">
          <li class="nav-item admin-only" id="sedePickerItem" style="display:none">
            <select id="sedePicker" class="form-select form-select-sm" style="min-width:140px">
              <option value="">— Sede —</option>
              ${SEDI.map(s => `<option value="${s}">${s}</option>`).join('')}
            </select>
          </li>
          <li class="nav-item">
            <button id="btnTheme" onclick="toggleTheme()" title="Cambia tema">
              <i class="bi bi-moon"></i>
            </button>
          </li>
          <li class="nav-item dropdown">
            <a class="nav-link dropdown-toggle" href="#" data-bs-toggle="dropdown">
              <i class="bi bi-person-circle me-1"></i><span id="navUserName">...</span>
            </a>
            <ul class="dropdown-menu dropdown-menu-end">
              <li>
                <button class="dropdown-item text-danger" onclick="doLogout()">
                  <i class="bi bi-box-arrow-right me-1"></i>Esci
                </button>
              </li>
            </ul>
          </li>
        </ul>
      </div>
    </div>
  </nav>`;
  $('#navbarContainer').html(html);

  $(document).on('change', '#sedePicker', function () {
    const sede = $(this).val();
    if (!sede) return;
    Api.post('/api/account/sede', { sede })
      .done(() => {
        updatePageTitle(sede);
        if (typeof onSedeChanged === 'function') onSedeChanged(sede);
      })
      .fail(() => alert('Errore nella selezione della sede.'));
  });
}

function syncSedePicker(sede) {
  if (sede) $('#sedePicker').val(sede);
}
