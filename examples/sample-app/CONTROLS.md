# Sample Shop: stable control texts and element ids

Both variants are made to be operated by an automation tool. The desktop program is found by
window titles, window classes and control texts; the browser variant by element ids. Neither
changes between runs, and the element ids do not change with the language.

## Desktop program (`sample-desktop.exe`)

Standard Windows Forms controls only. Buttons are system-style buttons (window class contains
`BUTTON`), text boxes contain `EDIT`, lists `SysListView32`, the tab control `SysTabControl32`,
labels `STATIC`. Font Tahoma 8.25 pt, no scaling, fixed positions (x, y, width, height in pixels
of the client area of their parent).

### Main window, title `Sample Shop`

Client area 900 x 600, at screen position 40, 40.

| Control | Text | Bounds |
|---|---|---|
| Tab control | pages `Customers`, `Articles`, `Invoices` | 8, 8, 884, 556 |
| Status label | `Ready` at start, then the last message | 10, 572, 880, 21 |

Every tab page has the same layout (positions inside the page):

| Control | Text | Bounds |
|---|---|---|
| List (details view, full row select) | columns see below | 8, 8, 860, 472 |
| Button 1 | `New customer` / `Edit article` / `New invoice` | 8, 490, 120, 26 |
| Button 2 | `Edit customer` / `Refresh articles` / `Mark paid` | 136, 490, 120, 26 |
| Button 3 | `Refresh customers` / (none) / `Refresh invoices` | 264, 490, 120, 26 |
| Label | `Selected id` | 680, 492, 70, 21 |
| Text box "Selected id" | id of the selected row; typing an id selects that row | 754, 492, 114, 21 |

List columns:

- Customers: `Id`, `Name`, `City`, `Group`, `Credit limit`
- Articles: `Id`, `SKU`, `Name`, `Price`, `Stock`, `Tax class`
- Invoices: `Id`, `Number`, `Customer`, `Date`, `Net`, `Discount`, `Tax`, `Gross`, `Status`

Rows are ordered by id; the first row starts about 26 pixels below the top of the list, each
row is about 17 pixels high.

### Dialog, title `Customer`

Client area 380 x 210, at screen position 100, 100. Text boxes in window order:

| Control | Text | Bounds |
|---|---|---|
| Label + text box 1 | `Name` | box 110, 12, 250, 21 |
| Label + text box 2 | `City` | box 110, 42, 250, 21 |
| Label + text box 3 | `Group` | box 110, 72, 40, 21 |
| Label + text box 4 | `Credit limit` | box 110, 102, 100, 21 |
| Status label | empty, or the reason why nothing was saved | 12, 136, 356, 21 |
| Button | `Save customer` | 154, 170, 110, 26 |
| Button | `Cancel` | 270, 170, 90, 26 |

### Dialog, title `Article`

Client area 380 x 180, at screen position 100, 100.

| Control | Text | Bounds |
|---|---|---|
| Label + text box 1 | `Name` | box 110, 12, 250, 21 |
| Label + text box 2 | `Price` | box 110, 42, 100, 21 |
| Label + text box 3 | `Stock` | box 110, 72, 100, 21 |
| Status label | | 12, 106, 356, 21 |
| Button | `Save article` | 154, 140, 110, 26 |
| Button | `Cancel` | 270, 140, 90, 26 |

### Dialog, title `New invoice`

Client area 620 x 470, at screen position 100, 100. Text boxes in window order: customer no.,
SKU, quantity, net, discount, tax, gross.

| Control | Text | Bounds |
|---|---|---|
| Label + text box 1 | `Customer no.` | box 110, 12, 80, 21 |
| List of lines | columns `SKU`, `Article`, `Quantity`, `Price`, `Line net` | 12, 44, 596, 220 |
| Label + text box 2 | `SKU` | box 56, 276, 100, 21 |
| Label + text box 3 | `Quantity` | box 236, 276, 60, 21 |
| Button | `Add line` | 312, 273, 90, 26 |
| Label + read-only text box 4 | `Net` | box 470, 310, 138, 21 |
| Label + read-only text box 5 | `Discount` | box 470, 336, 138, 21 |
| Label + read-only text box 6 | `Tax` | box 470, 362, 138, 21 |
| Label + read-only text box 7 | `Gross` | box 470, 388, 138, 21 |
| Status label | | 12, 436, 370, 21 |
| Button | `Save invoice` | 402, 434, 110, 26 |
| Button | `Cancel` | 518, 434, 90, 26 |

### Window `Sample Shop - connection error`

Shown instead of the main window when the database cannot be reached: one label with the reason
and the button `Close`.

### Status texts

Main window: `Ready`, `Customer <id> saved`, `Article <id> saved`, `Invoice <number> saved`,
`Invoice <number> marked paid`, `Customers refreshed`, `Articles refreshed`, `Invoices refreshed`,
`No customer selected`, `No article selected`, `No invoice selected`, `Unknown invoice`.

Dialogs: `<Field> is required`, `Group must be one of A, B, C`, `<Field> must be a number`,
`<Field> must be a whole number`, `<Field> must be at least 0`, `Line added`, `Unknown SKU`,
`Unknown customer`, `No lines`, `Quantity must be a whole number greater than 0`,
`Credit limit exceeded`, `The database did not accept the values`.

## Browser variant

Open with `?lang=en` to force English (for example `http://127.0.0.1:8780/?lang=en#/invoice`).
The English texts are the same as in the desktop program; amounts and dates are shown in the
format of the chosen language, and every table cell and every total carries the unformatted
value in the attribute `data-value`.

### Shell

| Id | Element |
|---|---|
| `#btn-toggle-nav` | shows or hides the navigation |
| `#app-name` | name of the application |
| `#nav-customer`, `#nav-article`, `#nav-invoice` | navigation links (addresses `#/customer`, `#/article`, `#/invoice`) |
| `#lang-en`, `#lang-de` | language switch |
| `#page-title`, `#page-meta` | title of the page, number of records |
| `#status` | status line of the main window |

### Page Customers

| Id | Element |
|---|---|
| `#btn-new-customer`, `#btn-edit-customer`, `#btn-refresh-customer` | buttons |
| `#customer-selected-id` | "Selected id" field; typing an id selects the row |
| `#customer-list` | table; rows `#customer-row-<id>` (the selected row has the class `sel`) |

Dialog `#dlg-customer`: `#customer-name`, `#customer-city`, `#customer-customer_group`,
`#customer-credit_limit`, `#btn-save-customer`, `#btn-cancel-customer`, `#btn-close-customer`,
status `#customer-status`.

### Page Articles

| Id | Element |
|---|---|
| `#btn-edit-article`, `#btn-refresh-article` | buttons |
| `#article-selected-id` | "Selected id" field |
| `#article-list` | table; rows `#article-row-<id>` |

Dialog `#dlg-article`: `#article-name`, `#article-unit_price`, `#article-stock`,
`#btn-save-article`, `#btn-cancel-article`, `#btn-close-article`, status `#article-status`.

### Page Invoices

| Id | Element |
|---|---|
| `#btn-new-invoice`, `#btn-mark-paid`, `#btn-refresh-invoice` | buttons |
| `#invoice-selected-id` | "Selected id" field |
| `#invoice-list` | table; rows `#invoice-row-<id>` |

Dialog `#dlg-invoice`: `#invoice-customer-no`, lines table `#invoice-lines` (rows
`#invoice-line-<n>`), `#invoice-sku`, `#invoice-quantity`, `#btn-add-line`, read-only totals
`#invoice-net`, `#invoice-discount`, `#invoice-tax`, `#invoice-gross`, `#btn-save-invoice`,
`#btn-cancel-invoice`, `#btn-close-invoice`, status `#invoice-status`.
