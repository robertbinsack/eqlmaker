-- SPDX-License-Identifier: AGPL-3.0-or-later
-- Copyright (c) 2026 Robert Binsack
--
-- Invented data of the sample application: 8 customers, 12 articles, 3 invoices.
-- The stock of the articles is the stock after the three invoices.

INSERT INTO customer (id, name, city, customer_group, credit_limit) VALUES
    (1, 'Alder Hardware',     'Northfield', 'A', 5000.00),
    (2, 'Birch Bakery',       'Eastbrook',  'B', 3000.00),
    (3, 'Cedar Books',        'Westhaven',  'C', 1000.00),
    (4, 'Dune Cycles',        'Southport',  'A', 2500.00),
    (5, 'Elm Garden Supply',  'Northfield', 'B', 8000.00),
    (6, 'Fern Tea House',     'Lakeside',   'C',  600.00),
    (7, 'Grove Office Goods', 'Eastbrook',  'A', 1200.00),
    (8, 'Harbor Crafts',      'Westhaven',  'B', 1500.00);

INSERT INTO article (id, sku, name, unit_price, stock, tax_class) VALUES
    (1,  'TL-100', 'Claw hammer',            12.50,  80, 'S'),
    (2,  'TL-110', 'Screwdriver set',        24.90,  45, 'S'),
    (3,  'TL-120', 'Tape measure 5 m',        7.95, 120, 'S'),
    (4,  'OF-200', 'Copy paper 500 sheets',   4.99, 300, 'S'),
    (5,  'OF-210', 'Ring binder',             2.35, 200, 'S'),
    (6,  'OF-220', 'Desk lamp',              39.00,  25, 'S'),
    (7,  'FD-300', 'Ground coffee 500 g',     6.49, 150, 'R'),
    (8,  'FD-310', 'Black tea 100 g',         3.95,  90, 'R'),
    (9,  'FD-320', 'Oat biscuits',            1.85, 240, 'R'),
    (10, 'BK-400', 'Pocket atlas',           14.00,  40, 'R'),
    (11, 'BK-410', 'Notebook A5',             3.50, 180, 'S'),
    (12, 'GD-500', 'Watering can 10 l',      18.75,  35, 'S');

INSERT INTO invoice (id, number, customer_id, invoice_date, net, discount_percent, discount, tax, gross, status) VALUES
    (1, 'INV-2026-00001', 1, DATE '2026-01-15',  562.90, 5.00, 28.15,  94.21,  628.96, 'paid'),
    (2, 'INV-2026-00002', 3, DATE '2026-02-03',   84.00, 0.00,  0.00,   7.56,   91.56, 'open'),
    (3, 'INV-2026-00003', 5, DATE '2026-02-20', 1005.00, 3.00, 30.15, 185.22, 1160.07, 'open');

INSERT INTO invoice_line (id, invoice_id, article_id, quantity, unit_price, line_net) VALUES
    (1, 1,  2, 20, 24.90, 498.00),
    (2, 1,  7, 10,  6.49,  64.90),
    (3, 2, 10,  5, 14.00,  70.00),
    (4, 2, 11,  4,  3.50,  14.00),
    (5, 3,  6, 20, 39.00, 780.00),
    (6, 3, 12, 12, 18.75, 225.00);

INSERT INTO stock_movement (id, article_id, quantity, reason, invoice_id) VALUES
    (1,  2, -20, 'invoice', 1),
    (2,  7, -10, 'invoice', 1),
    (3, 10,  -5, 'invoice', 2),
    (4, 11,  -4, 'invoice', 2),
    (5,  6, -20, 'invoice', 3),
    (6, 12, -12, 'invoice', 3);

INSERT INTO counter (name, value) VALUES ('invoice', 3);

SELECT setval(pg_get_serial_sequence('customer', 'id'), (SELECT max(id) FROM customer));
SELECT setval(pg_get_serial_sequence('article', 'id'), (SELECT max(id) FROM article));
SELECT setval(pg_get_serial_sequence('invoice', 'id'), (SELECT max(id) FROM invoice));
SELECT setval(pg_get_serial_sequence('invoice_line', 'id'), (SELECT max(id) FROM invoice_line));
SELECT setval(pg_get_serial_sequence('stock_movement', 'id'), (SELECT max(id) FROM stock_movement));
