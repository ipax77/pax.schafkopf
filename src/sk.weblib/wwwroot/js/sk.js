var noSleep = new NoSleep();
var wakeLockEnabled = false;

function openModalById(id) {
    const modalElement = document.getElementById(id);
    if (!modalElement) return;

    let modal = bootstrap.Modal.getInstance(modalElement);
    if (!modal) {
        modal = new bootstrap.Modal(modalElement);
    }
    modal.show();
}

function closeModalById(id) {
    const modalElement = document.getElementById(id);
    if (!modalElement) return;

    const modal = bootstrap.Modal.getInstance(modalElement);
    if (modal) {
        modal.hide();
    }
}

function scrollModalToTop(modalId) {
    const modal = document.getElementById(modalId);
    if (modal) {
        const modalBody = modal.querySelector('.modal-body');
        if (modalBody) {
            modalBody.scrollTop = 0;
        }
    }
};

function trickAnimation(id) {
    var element = document.getElementById(id);
    if (element != null) {
        element.classList.add("collecttrick");
        setTimeout(() => { element.classList.remove("collecttrick"); }, 2500);
    }
}

function screenSleep() {
    var toggleEl = document.querySelector("#screentoggle");
    toggleEl.addEventListener('click', function () {
        if (!wakeLockEnabled) {
            noSleep.enable(); // keep the screen on!
            wakeLockEnabled = true;
            toggleEl.value = "Display an";
            toggleEl.classList.remove("btn-outline-secondary");
            toggleEl.classList.add("btn-outline-primary");
        } else {
            noSleep.disable(); // let the screen turn off.
            wakeLockEnabled = false;
            toggleEl.value = "Display aus";
            toggleEl.classList.remove("btn-outline-primary");
            toggleEl.classList.add("btn-outline-secondary");
        }
    }, false);
}

function updateUrlSilently(url) {
    history.replaceState(history.state, document.title, url);
}

function storeConnectInfo(info) {
    localStorage.setItem("connectInfo", JSON.stringify(info));
}

function getConnectInfo() {
    const value = localStorage.getItem("connectInfo");
    return value ? JSON.parse(value) : null;
}

function blazorSetPointerCapture (elementId, pointerId) {
    var element = document.getElementById(elementId);
    if (!element) {
        return;
    }
    element.setPointerCapture(pointerId);
}

function getCardHeight() {
    const el = document.createElement("div");
    el.style.position = "absolute";
    el.style.visibility = "hidden";
    el.style.height = "var(--cardheight)";
    el.style.width = "var(--cardwidth)";
    el.style.maxHeight = "var(--cardmaxheight)";
    el.style.maxWidth = "var(--cardmaxwidth)";

    document.body.appendChild(el);

    const rect = el.getBoundingClientRect();

    document.body.removeChild(el);

    return rect.height;
}