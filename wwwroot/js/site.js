/**
 * Jira Sheet Board - Modern Atlassian Client System
 * Compact, High-Density Read-Only Interface with 3rd-Party GLightbox HD Viewer
 */

let gLightboxInstance = null;
let currentModalIssue = null;
let modalInstance = null;

document.addEventListener("DOMContentLoaded", function () {
    initDensityPreference();
    initKeyboardShortcuts();
    initSearchFilter();
    initMultiFilters();
    initIssueDetailModal();
    initCopyKeyButton();
    initTableSorting();
    initImageFallbacks();
    initGLightbox();
});

// --------------------------------------------------------------------------
// 1. Toast Notification Helper
// --------------------------------------------------------------------------
function showJiraToast(message, isSuccess = true) {
    const toastEl = document.getElementById("jiraToast");
    if (!toastEl) return;
    const msgEl = document.getElementById("jiraToastMsg");
    const iconEl = document.getElementById("jiraToastIcon");

    if (msgEl) msgEl.textContent = message;
    if (iconEl) {
        if (isSuccess) {
            iconEl.className = "bi bi-check-circle-fill text-success fs-6";
        } else {
            iconEl.className = "bi bi-exclamation-triangle-fill text-warning fs-6";
        }
    }

    const toast = bootstrap.Toast.getOrCreateInstance(toastEl, { delay: 2800 });
    toast.show();
}

// --------------------------------------------------------------------------
// 2. Density Preference (Compact vs. Comfortable)
// --------------------------------------------------------------------------
function initDensityPreference() {
    const toggleBtn = document.getElementById("densityToggleBtn");
    const toggleText = document.getElementById("densityToggleText");
    const savedDensity = localStorage.getItem("jiraBoardDensity") || "compact";

    applyDensity(savedDensity === "compact");

    if (toggleBtn) {
        toggleBtn.addEventListener("click", function () {
            const isCurrentlyCompact = document.body.classList.contains("compact-density");
            const newCompact = !isCurrentlyCompact;
            applyDensity(newCompact);
            localStorage.setItem("jiraBoardDensity", newCompact ? "compact" : "comfortable");
            showJiraToast(newCompact ? "Density set to Compact" : "Density set to Comfortable", true);
        });
    }

    function applyDensity(isCompact) {
        if (isCompact) {
            document.body.classList.add("compact-density");
            if (toggleText) toggleText.textContent = "Compact";
        } else {
            document.body.classList.remove("compact-density");
            if (toggleText) toggleText.textContent = "Comfortable";
        }
    }
}

// --------------------------------------------------------------------------
// 3. Keyboard Shortcuts (/ to search, 1 Board, 2 Table, 3 Gallery, Esc)
// --------------------------------------------------------------------------
function initKeyboardShortcuts() {
    document.addEventListener("keydown", function (e) {
        const activeTag = document.activeElement ? document.activeElement.tagName.toLowerCase() : "";
        const isEditing = activeTag === "input" || activeTag === "textarea" || activeTag === "select";

        if (e.key === "Escape") {
            const searchInput = document.getElementById("clientSearchInput");
            if (searchInput && document.activeElement === searchInput) {
                searchInput.blur();
                return;
            }
        }

        if (isEditing) return;

        // "/" key -> Focus instant search
        if (e.key === "/" && !e.ctrlKey && !e.metaKey && !e.altKey) {
            e.preventDefault();
            const searchInput = document.getElementById("clientSearchInput");
            if (searchInput) {
                searchInput.focus();
                searchInput.select();
            }
            return;
        }

        // "1" -> Switch to Board view
        if (e.key === "1") {
            switchView("board");
            return;
        }

        // "2" -> Switch to Table view
        if (e.key === "2") {
            switchView("table");
            return;
        }

        // "3" -> Switch to Gallery view
        if (e.key === "3") {
            switchView("gallery");
            return;
        }
    });

    function switchView(targetView) {
        try {
            const url = new URL(window.location.href);
            if (url.searchParams.get("view") === targetView) return;
            url.searchParams.set("view", targetView);
            window.location.href = url.toString();
        } catch { }
    }
}

// --------------------------------------------------------------------------
// 4. 3rd-Party GLightbox Initialization
// --------------------------------------------------------------------------
function initGLightbox() {
    if (typeof GLightbox === "undefined") return;

    try {
        gLightboxInstance = GLightbox({
            selector: ".glightbox-hero, .jira-gallery-img-link",
            touchNavigation: true,
            loop: true,
            zoomable: true,
            autoplayVideos: false
        });
    } catch (err) {
        console.warn("GLightbox initialization warning:", err);
    }
}

function refreshGLightbox() {
    if (gLightboxInstance) {
        try {
            gLightboxInstance.destroy();
        } catch { }
    }
    initGLightbox();
}

// --------------------------------------------------------------------------
// 5. Client-Side Instant Search Filter
// --------------------------------------------------------------------------
function initSearchFilter() {
    const searchInput = document.getElementById("clientSearchInput");
    const clearBtn = document.getElementById("searchClearBtn");
    if (!searchInput) return;

    searchInput.addEventListener("input", function () {
        const query = this.value.trim();
        if (clearBtn) {
            clearBtn.style.display = query.length > 0 ? "block" : "none";
        }
        applyClientFilters();
    });

    if (clearBtn) {
        clearBtn.addEventListener("click", function () {
            searchInput.value = "";
            clearBtn.style.display = "none";
            applyClientFilters();
            searchInput.focus();
        });
    }
}

// --------------------------------------------------------------------------
// 6. Multi-Select Filter System (Status, Assignee, Priority, Types, Photos)
// --------------------------------------------------------------------------
function initMultiFilters() {
    document.querySelectorAll(".jira-filter-dropdown .filter-checkbox").forEach(function (cb) {
        cb.addEventListener("change", function () {
            applyClientFilters();
        });
    });

    document.querySelectorAll(".jira-filter-dropdown").forEach(function (dropdown) {
        const searchInput = dropdown.querySelector(".jira-filter-search");
        const optionsList = dropdown.querySelector(".jira-filter-options-list");
        if (searchInput && optionsList) {
            searchInput.addEventListener("input", function () {
                const term = this.value.trim().toLowerCase();
                optionsList.querySelectorAll(".jira-filter-option").forEach(function (opt) {
                    const name = opt.querySelector(".option-name")?.textContent.toLowerCase() || "";
                    opt.style.display = name.includes(term) ? "" : "none";
                });
            });
        }

        const selectAllBtn = dropdown.querySelector(".select-all-btn");
        if (selectAllBtn && optionsList) {
            selectAllBtn.addEventListener("click", function () {
                optionsList.querySelectorAll(".jira-filter-option:not([style*='display: none']) .filter-checkbox").forEach(function (cb) {
                    cb.checked = true;
                });
                applyClientFilters();
            });
        }

        const clearAllBtn = dropdown.querySelector(".clear-all-btn");
        if (clearAllBtn && optionsList) {
            clearAllBtn.addEventListener("click", function () {
                optionsList.querySelectorAll(".filter-checkbox").forEach(function (cb) {
                    cb.checked = false;
                });
                applyClientFilters();
            });
        }
    });

    const photosBtn = document.getElementById("photosOnlyToggleBtn");
    const photosHidden = document.getElementById("withPhotosHidden");
    if (photosBtn && photosHidden) {
        photosBtn.addEventListener("click", function () {
            const isCurrentlyOn = photosHidden.value === "true";
            photosHidden.value = isCurrentlyOn ? "false" : "true";
            photosBtn.classList.toggle("active", !isCurrentlyOn);
            applyClientFilters();
        });
    }
}

function removeFilter(type, value) {
    const cb = document.querySelector(`.jira-filter-dropdown[data-filter-type="${type}"] input[value="${value}"]`);
    if (cb) {
        cb.checked = false;
    }
    applyClientFilters();
}

function removeSearchFilter() {
    const searchInput = document.getElementById("clientSearchInput");
    const clearBtn = document.getElementById("searchClearBtn");
    if (searchInput) searchInput.value = "";
    if (clearBtn) clearBtn.style.display = "none";
    applyClientFilters();
}

function togglePhotosFilter(enable) {
    const photosBtn = document.getElementById("photosOnlyToggleBtn");
    const photosHidden = document.getElementById("withPhotosHidden");
    if (photosHidden) photosHidden.value = enable ? "true" : "false";
    if (photosBtn) photosBtn.classList.toggle("active", enable);
    applyClientFilters();
}

function clearAllFilters() {
    document.querySelectorAll(".jira-filter-dropdown .filter-checkbox").forEach(function (cb) {
        cb.checked = false;
    });
    const searchInput = document.getElementById("clientSearchInput");
    const clearBtn = document.getElementById("searchClearBtn");
    if (searchInput) searchInput.value = "";
    if (clearBtn) clearBtn.style.display = "none";

    const photosBtn = document.getElementById("photosOnlyToggleBtn");
    const photosHidden = document.getElementById("withPhotosHidden");
    if (photosHidden) photosHidden.value = "false";
    if (photosBtn) photosBtn.classList.remove("active");

    applyClientFilters();
}

function applyClientFilters() {
    const searchInput = document.getElementById("clientSearchInput");
    const searchQuery = searchInput ? searchInput.value.trim().toLowerCase() : "";

    const selectedStatuses = getCheckedValues("status");
    const selectedAssignees = getCheckedValues("assignee");
    const selectedPriorities = getCheckedValues("priority");
    const selectedTypes = getCheckedValues("type");

    const photosHidden = document.getElementById("withPhotosHidden");
    const photosOnly = photosHidden ? photosHidden.value === "true" : false;

    let totalVisible = 0;

    function matchesFilter(cardStatus, cardAssignee, cardPriority, cardType, cardHasPhoto, cardSearchText) {
        const sMatch = selectedStatuses.length === 0 || selectedStatuses.includes(cardStatus.toLowerCase());
        const aMatch = selectedAssignees.length === 0 || selectedAssignees.includes(cardAssignee.toLowerCase());
        const pMatch = selectedPriorities.length === 0 || selectedPriorities.includes(cardPriority.toLowerCase());
        const tMatch = selectedTypes.length === 0 || selectedTypes.includes(cardType.toLowerCase());
        const photoMatch = !photosOnly || cardHasPhoto;
        const searchMatch = !searchQuery || cardSearchText.includes(searchQuery);

        return sMatch && aMatch && pMatch && tMatch && photoMatch && searchMatch;
    }

    // 1. Kanban cards
    const cards = document.querySelectorAll(".jira-card");
    cards.forEach(function (card) {
        const cStatus = card.getAttribute("data-status") || "";
        const cAssignee = card.getAttribute("data-assignee") || "Unassigned";
        const cPriority = card.getAttribute("data-priority") || "";
        const cType = card.getAttribute("data-type") || "Bug";
        const cHasPhoto = card.getAttribute("data-has-photo") === "true";
        const cSearch = (card.getAttribute("data-searchable-text") || card.textContent).toLowerCase();

        const isVisible = matchesFilter(cStatus, cAssignee, cPriority, cType, cHasPhoto, cSearch);
        card.style.display = isVisible ? "" : "none";
        if (isVisible) totalVisible++;
    });

    // Update column counters & visibility
    document.querySelectorAll(".jira-column").forEach(function (col) {
        const colStatus = (col.getAttribute("data-column-status") || "").toLowerCase();
        if (selectedStatuses.length === 0 || selectedStatuses.includes(colStatus)) {
            col.style.display = "";
        } else {
            col.style.display = "none";
        }

        const visibleCards = col.querySelectorAll(".jira-card:not([style*='display: none'])");
        const countBadge = col.querySelector(".jira-column-count");
        if (countBadge) {
            countBadge.textContent = visibleCards.length;
        }
    });

    // 2. Table rows
    const tableRows = document.querySelectorAll(".jira-table tbody tr");
    if (tableRows.length > 0) {
        totalVisible = 0;
        tableRows.forEach(function (row) {
            const rStatus = row.getAttribute("data-status") || "";
            const rAssignee = row.getAttribute("data-assignee") || "Unassigned";
            const rPriority = row.getAttribute("data-priority") || "";
            const rType = row.getAttribute("data-type") || "Bug";
            const rHasPhoto = row.getAttribute("data-has-photo") === "true";
            const rSearch = (row.getAttribute("data-searchable-text") || row.textContent).toLowerCase();

            const isVisible = matchesFilter(rStatus, rAssignee, rPriority, rType, rHasPhoto, rSearch);
            row.style.display = isVisible ? "" : "none";
            if (isVisible) totalVisible++;
        });
    }

    // 3. Gallery cards
    const galleryCards = document.querySelectorAll(".jira-gallery-card");
    if (galleryCards.length > 0 && tableRows.length === 0 && cards.length === 0) {
        totalVisible = 0;
        galleryCards.forEach(function (card) {
            const gStatus = card.getAttribute("data-status") || "";
            const gAssignee = card.getAttribute("data-assignee") || "Unassigned";
            const gPriority = card.getAttribute("data-priority") || "";
            const gType = card.getAttribute("data-type") || "Bug";
            const gHasPhoto = card.getAttribute("data-has-photo") === "true";
            const gSearch = (card.getAttribute("data-searchable-text") || card.textContent).toLowerCase();

            const isVisible = matchesFilter(gStatus, gAssignee, gPriority, gType, gHasPhoto, gSearch);
            card.style.display = isVisible ? "" : "none";
            if (isVisible) totalVisible++;
        });
    }

    const counterEl = document.getElementById("visibleCardsCounter");
    if (counterEl) {
        counterEl.textContent = totalVisible;
    }

    updateFilterButtonBadge("status", selectedStatuses);
    updateFilterButtonBadge("assignee", selectedAssignees);
    updateFilterButtonBadge("priority", selectedPriorities);
    updateFilterButtonBadge("type", selectedTypes);

    updateActiveChipsBar(searchQuery, selectedStatuses, selectedAssignees, selectedPriorities, selectedTypes, photosOnly);
    syncUrlParams(searchQuery, selectedStatuses, selectedAssignees, selectedPriorities, selectedTypes, photosOnly);
}

function getCheckedValues(name) {
    const list = [];
    document.querySelectorAll(`.jira-filter-dropdown[data-filter-type="${name}"] input[type="checkbox"]:checked`).forEach(function (cb) {
        list.push(cb.value);
    });
    return list;
}

function updateFilterButtonBadge(type, selectedItems) {
    const dropdown = document.querySelector(`.jira-filter-dropdown[data-filter-type="${type}"]`);
    if (!dropdown) return;
    const btn = dropdown.querySelector(".jira-filter-btn");
    const counter = dropdown.querySelector(".jira-filter-counter");
    if (!btn) return;

    if (selectedItems.length > 0) {
        btn.classList.add("active");
        if (counter) {
            counter.textContent = selectedItems.length;
            counter.style.display = "";
        }
    } else {
        btn.classList.remove("active");
        if (counter) {
            counter.style.display = "none";
        }
    }
}

function updateActiveChipsBar(search, statuses, assignees, priorities, types, photosOnly) {
    const bar = document.getElementById("activeChipsBar");
    const list = document.getElementById("activeChipsList");
    if (!bar || !list) return;

    let html = "";
    let hasAny = false;

    if (search) {
        hasAny = true;
        html += `<span class="jira-chip" data-chip-type="search">
            Search: "<strong>${escapeHtml(search)}</strong>"
            <button type="button" class="jira-chip-remove" title="Remove search filter" onclick="removeSearchFilter()">&times;</button>
        </span>`;
    }

    statuses.forEach(function (s) {
        hasAny = true;
        html += `<span class="jira-chip" data-chip-type="status">
            Status: <strong>${escapeHtml(s)}</strong>
            <button type="button" class="jira-chip-remove" title="Remove ${escapeAttr(s)}" onclick="removeFilter('status', '${escapeAttr(s)}')">&times;</button>
        </span>`;
    });

    assignees.forEach(function (a) {
        hasAny = true;
        html += `<span class="jira-chip" data-chip-type="assignee">
            Assignee: <strong>${escapeHtml(a)}</strong>
            <button type="button" class="jira-chip-remove" title="Remove ${escapeAttr(a)}" onclick="removeFilter('assignee', '${escapeAttr(a)}')">&times;</button>
        </span>`;
    });

    priorities.forEach(function (p) {
        hasAny = true;
        html += `<span class="jira-chip" data-chip-type="priority">
            Priority: <strong>${escapeHtml(p)}</strong>
            <button type="button" class="jira-chip-remove" title="Remove ${escapeAttr(p)}" onclick="removeFilter('priority', '${escapeAttr(p)}')">&times;</button>
        </span>`;
    });

    types.forEach(function (t) {
        hasAny = true;
        html += `<span class="jira-chip" data-chip-type="type">
            Type: <strong>${escapeHtml(t)}</strong>
            <button type="button" class="jira-chip-remove" title="Remove ${escapeAttr(t)}" onclick="removeFilter('type', '${escapeAttr(t)}')">&times;</button>
        </span>`;
    });

    if (photosOnly) {
        hasAny = true;
        html += `<span class="jira-chip" data-chip-type="photos">
            <i class="bi bi-camera-fill text-primary"></i> <strong>Photos Only</strong>
            <button type="button" class="jira-chip-remove" title="Show all cards" onclick="togglePhotosFilter(false)">&times;</button>
        </span>`;
    }

    list.innerHTML = html;
    bar.style.display = hasAny ? "flex" : "none";
}

function syncUrlParams(search, statuses, assignees, priorities, types, photosOnly) {
    if (!window.history || !window.history.replaceState) return;
    try {
        const url = new URL(window.location.href);
        url.searchParams.delete("status");
        url.searchParams.delete("assignee");
        url.searchParams.delete("priority");
        url.searchParams.delete("type");
        url.searchParams.delete("search");
        url.searchParams.delete("withPhotos");

        if (search) url.searchParams.set("search", search);
        statuses.forEach(s => url.searchParams.append("status", s));
        assignees.forEach(a => url.searchParams.append("assignee", a));
        priorities.forEach(p => url.searchParams.append("priority", p));
        types.forEach(t => url.searchParams.append("type", t));
        if (photosOnly) url.searchParams.set("withPhotos", "true");

        window.history.replaceState({}, "", url.toString());
    } catch { }
}

// --------------------------------------------------------------------------
// 7. Issue Detail Modal (Read-Only + GLightbox HD Viewer + QA Verification)
// --------------------------------------------------------------------------
function initIssueDetailModal() {
    const modalEl = document.getElementById("issueDetailModal");
    if (!modalEl) return;

    modalInstance = new bootstrap.Modal(modalEl);

    // Open modal on card / row click
    document.addEventListener("click", function (e) {
        const targetCard = e.target.closest("[data-issue-id]");
        if (targetCard) {
            if (e.target.closest("button") || e.target.closest("a") || e.target.closest("input")) return;
            const issueId = targetCard.getAttribute("data-issue-id");
            if (issueId) {
                openIssueModal(issueId);
            }
        }
    });
}

async function openIssueModal(issueId) {
    try {
        const response = await fetch(`/Home/GetIssue?id=${encodeURIComponent(issueId)}`);
        if (!response.ok) {
            showJiraToast("Failed to fetch issue details.", false);
            return;
        }

        const issue = await response.json();
        currentModalIssue = issue;
        populateModalData(issue);
        modalInstance.show();
    } catch (err) {
        console.error("Error opening modal:", err);
        showJiraToast("Network error loading issue.", false);
    }
}

function populateModalData(issue) {
    const issueKeyEl = document.getElementById("modalIssueKey");
    if (issueKeyEl) issueKeyEl.textContent = issue.key || "ISSUE";

    const titleEl = document.getElementById("modalIssueTitle");
    if (titleEl) titleEl.textContent = issue.title || "Untitled";

    // Issue Type Badge
    const typeBadge = document.getElementById("modalTypeBadge");
    if (typeBadge) {
        typeBadge.textContent = (issue.issueType || "Bug").substring(0, 1).toUpperCase();
        typeBadge.className = "jira-type-icon " + getTypeBadgeClass(issue.issueType);
        typeBadge.title = issue.issueType || "Bug";
    }

    // Status Badge
    const statusBadge = document.getElementById("modalStatusBadge");
    if (statusBadge) {
        statusBadge.textContent = issue.status || "Open";
        statusBadge.className = "badge-status " + getStatusBadgeClass(issue.status);
    }

    // Assignee
    const assigneeVal = issue.assignee || "Unassigned";
    const assigneeName = document.getElementById("modalAssigneeName");
    const assigneeAvatar = document.getElementById("modalAssigneeAvatar");
    if (assigneeName) assigneeName.textContent = assigneeVal;
    if (assigneeAvatar) {
        assigneeAvatar.textContent = getInitials(assigneeVal);
        assigneeAvatar.className = "jira-avatar-circle " + (assigneeVal === "Unassigned" ? "jira-avatar-unassigned" : "");
    }

    // Reporter
    const reporterName = document.getElementById("modalReporterName");
    if (reporterName) reporterName.textContent = issue.reporter || "QA Team";

    // Priority
    const prioEl = document.getElementById("modalPriority");
    if (prioEl) {
        prioEl.innerHTML = `<span class="jira-priority-icon prio-${(issue.priority || "medium").toLowerCase()}">${getPriorityIconHtml(issue.priority)}</span> ${issue.priority || "Medium"}`;
    }

    // Due Date
    const dueEl = document.getElementById("modalDueDate");
    if (dueEl) dueEl.textContent = issue.dueDate ? formatDate(issue.dueDate) : "-";

    // Created Date
    const createdEl = document.getElementById("modalCreatedDate");
    if (createdEl) createdEl.textContent = issue.createdDate ? formatDate(issue.createdDate) : "-";

    // Description
    const descEl = document.getElementById("modalDescription");
    if (descEl) descEl.textContent = issue.description || "No description provided.";

    // QA Reproduction & Verification fields
    const custom = issue.customFields || {};
    const stepsView = document.getElementById("modalStepsView");
    if (stepsView) stepsView.textContent = custom["Steps to Reproduce"] || "-";

    const actualView = document.getElementById("modalActualView");
    if (actualView) actualView.textContent = custom["Actual Result"] || "-";

    const expectedView = document.getElementById("modalExpectedView");
    if (expectedView) expectedView.textContent = custom["Expected Result"] || "-";

    const commentsView = document.getElementById("modalCommentsView");
    if (commentsView) commentsView.textContent = custom["Comments"] || "-";

    // Tags
    const tagsContainer = document.getElementById("modalTagsContainer");
    if (tagsContainer) {
        tagsContainer.innerHTML = "";
        if (issue.tags && issue.tags.length > 0) {
            issue.tags.forEach(function (tag) {
                const span = document.createElement("span");
                span.className = "jira-tag-pill";
                span.textContent = tag;
                tagsContainer.appendChild(span);
            });
        } else {
            tagsContainer.innerHTML = '<span class="text-muted small">None</span>';
        }
    }

    // Photo Gallery with GLightbox Fullscreen Viewer
    const photoSection = document.getElementById("modalPhotoSection");
    const heroImg = document.getElementById("modalHeroPhoto");
    const heroLink = document.getElementById("modalHeroPhotoLink");
    const countBadge = document.getElementById("modalPhotoCountBadge");
    const thumbContainer = document.getElementById("modalThumbnailsContainer");

    if (issue.photos && issue.photos.length > 0) {
        photoSection.style.display = "block";
        const primaryPhoto = issue.photos[0];

        if (heroImg) heroImg.src = primaryPhoto;
        if (heroLink) {
            heroLink.href = primaryPhoto;
            heroLink.setAttribute("data-gallery", `issue-${issue.id || "gallery"}`);
            heroLink.setAttribute("data-title", `${issue.key}: ${issue.title}`);
        }
        if (countBadge) countBadge.textContent = `${issue.photos.length} photo${issue.photos.length > 1 ? "s" : ""}`;

        if (thumbContainer) {
            thumbContainer.innerHTML = "";
            if (issue.photos.length > 1) {
                issue.photos.forEach(function (photoUrl, idx) {
                    const thumb = document.createElement("img");
                    thumb.src = photoUrl;
                    thumb.alt = `Screenshot ${idx + 1}`;
                    thumb.className = "jira-detail-thumb" + (idx === 0 ? " active" : "");
                    thumb.addEventListener("click", function () {
                        if (heroImg) heroImg.src = photoUrl;
                        if (heroLink) {
                            heroLink.href = photoUrl;
                        }
                        thumbContainer.querySelectorAll(".jira-detail-thumb").forEach(t => t.classList.remove("active"));
                        thumb.classList.add("active");
                        refreshGLightbox();
                    });
                    thumbContainer.appendChild(thumb);
                });
            }
        }
        refreshGLightbox();
    } else {
        if (photoSection) photoSection.style.display = "none";
    }

    // Dynamic Custom Sheet Attributes
    const customFieldsContainer = document.getElementById("modalCustomFields");
    if (customFieldsContainer) {
        customFieldsContainer.innerHTML = "";
        const keys = Object.keys(custom);
        const standardQaKeys = ["Steps to Reproduce", "Actual Result", "Expected Result", "Comments"];

        let attrCount = 0;
        keys.forEach(function (key) {
            if (standardQaKeys.includes(key)) return; // already in QA section
            const val = custom[key];
            if (val && !val.startsWith("http")) {
                attrCount++;
                const col = document.createElement("div");
                col.className = "col-md-6";
                col.innerHTML = `
                    <div class="jira-custom-field-card">
                        <div class="jira-custom-field-name">${escapeHtml(key)}</div>
                        <div class="jira-custom-field-val">${escapeHtml(val)}</div>
                    </div>
                `;
                customFieldsContainer.appendChild(col);
            }
        });

        if (attrCount === 0) {
            customFieldsContainer.innerHTML = '<div class="col-12"><span class="text-muted small">No extra attributes.</span></div>';
        }
    }
}

// --------------------------------------------------------------------------
// 8. Copy Issue Key Button
// --------------------------------------------------------------------------
function initCopyKeyButton() {
    const copyBtn = document.getElementById("copyKeyBtn");
    if (!copyBtn) return;

    copyBtn.addEventListener("click", function () {
        const keyEl = document.getElementById("modalIssueKey");
        const key = keyEl ? keyEl.textContent.trim() : "";
        if (!key) return;

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(key).then(function () {
                onCopySuccess(key);
            }).catch(function () {
                fallbackCopy(key);
            });
        } else {
            fallbackCopy(key);
        }
    });

    function fallbackCopy(text) {
        const textarea = document.createElement("textarea");
        textarea.value = text;
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand("copy");
            onCopySuccess(text);
        } catch { }
        document.body.removeChild(textarea);
    }

    function onCopySuccess(key) {
        copyBtn.innerHTML = '<i class="bi bi-check2"></i> Copied!';
        copyBtn.classList.add("copied");
        showJiraToast(`Copied ${key} to clipboard`, true);
        setTimeout(function () {
            copyBtn.innerHTML = '<i class="bi bi-clipboard"></i> Copy Key';
            copyBtn.classList.remove("copied");
        }, 1600);
    }
}

// --------------------------------------------------------------------------
// 9. Interactive Table Column Sorting
// --------------------------------------------------------------------------
function initTableSorting() {
    const table = document.querySelector(".jira-table");
    if (!table) return;

    const headers = table.querySelectorAll("th.sortable");
    headers.forEach(function (header, colIndex) {
        header.addEventListener("click", function () {
            const currentAsc = header.getAttribute("data-asc") === "true";
            const newAsc = !currentAsc;
            header.setAttribute("data-asc", newAsc);

            headers.forEach(h => {
                const arrow = h.querySelector(".sort-arrow");
                if (arrow) arrow.textContent = "⇅";
            });
            const activeArrow = header.querySelector(".sort-arrow");
            if (activeArrow) activeArrow.textContent = newAsc ? "↑" : "↓";

            sortTable(table, colIndex, newAsc);
        });
    });
}

function sortTable(table, colIndex, asc) {
    const tbody = table.querySelector("tbody");
    if (!tbody) return;
    const rows = Array.from(tbody.querySelectorAll("tr"));

    rows.sort(function (a, b) {
        const aCell = a.children[colIndex]?.innerText.trim() || "";
        const bCell = b.children[colIndex]?.innerText.trim() || "";

        const aDate = Date.parse(aCell);
        const bDate = Date.parse(bCell);
        if (!isNaN(aDate) && !isNaN(bDate)) {
            return asc ? aDate - bDate : bDate - aDate;
        }

        return asc ? aCell.localeCompare(bCell) : bCell.localeCompare(aCell);
    });

    rows.forEach(row => tbody.appendChild(row));
}

// --------------------------------------------------------------------------
// 10. Image Fallbacks
// --------------------------------------------------------------------------
function initImageFallbacks() {
    document.querySelectorAll("img").forEach(function (img) {
        img.addEventListener("error", function () {
            if (!this.getAttribute("data-fallback-applied")) {
                this.setAttribute("data-fallback-applied", "true");
                this.src = "/images/placeholder.svg";
            }
        });
    });
}

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------
function getTypeBadgeClass(type) {
    const t = (type || "").toLowerCase();
    if (t.includes("bug")) return "jira-type-bug";
    if (t.includes("story")) return "jira-type-story";
    if (t.includes("epic")) return "jira-type-epic";
    return "jira-type-task";
}

function getStatusBadgeClass(status) {
    const s = (status || "").toLowerCase();
    if (s.includes("progress")) return "badge-status-inprogress";
    if (s.includes("review") || s.includes("test")) return "badge-status-inreview";
    if (s.includes("done") || s.includes("closed") || s.includes("complete")) return "badge-status-done";
    return "badge-status-todo";
}

function getPriorityIconHtml(priority) {
    const p = (priority || "").toLowerCase();
    if (p === "critical" || p === "highest") return "▲▲";
    if (p === "major" || p === "high") return "▲";
    if (p === "minor" || p === "low") return "▼";
    if (p === "lowest") return "▼▼";
    return "■";
}

function getInitials(name) {
    if (!name || name === "Unassigned") return "?";
    const parts = name.trim().split(" ");
    if (parts.length >= 2) {
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }
    return name.substring(0, Math.min(2, name.length)).toUpperCase();
}

function formatDate(isoStr) {
    try {
        const d = new Date(isoStr);
        return d.toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
    } catch {
        return isoStr;
    }
}

function escapeHtml(text) {
    if (!text) return "";
    const map = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#039;' };
    return text.toString().replace(/[&<>"']/g, function (m) { return map[m]; });
}

function escapeAttr(str) {
    if (!str) return "";
    return str.replace(/'/g, "\\'").replace(/"/g, "&quot;");
}
