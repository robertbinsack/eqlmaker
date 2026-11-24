-- SPDX-License-Identifier: AGPL-3.0-or-later
-- Copyright (c) 2026 Robert Binsack
--
-- Tables of the sample application "Sample Shop" (schema public).

CREATE TABLE customer (
    id             serial PRIMARY KEY,
    name           text NOT NULL,
    city           text NOT NULL,
    customer_group char(1) NOT NULL CHECK (customer_group IN ('A', 'B', 'C')),
    credit_limit   numeric(10,2) NOT NULL CHECK (credit_limit >= 0)
);

CREATE TABLE article (
    id         serial PRIMARY KEY,
    sku        text NOT NULL UNIQUE,
    name       text NOT NULL,
    unit_price numeric(10,2) NOT NULL CHECK (unit_price >= 0),
    stock      integer NOT NULL,
    tax_class  char(1) NOT NULL CHECK (tax_class IN ('S', 'R'))
);

CREATE TABLE invoice (
    id               serial PRIMARY KEY,
    number           text NOT NULL UNIQUE,
    customer_id      integer NOT NULL REFERENCES customer (id),
    invoice_date     date NOT NULL DEFAULT current_date,
    net              numeric(10,2) NOT NULL,
    discount_percent numeric(4,2) NOT NULL,
    discount         numeric(10,2) NOT NULL,
    tax              numeric(10,2) NOT NULL,
    gross            numeric(10,2) NOT NULL,
    status           text NOT NULL CHECK (status IN ('open', 'paid'))
);

CREATE TABLE invoice_line (
    id         serial PRIMARY KEY,
    invoice_id integer NOT NULL REFERENCES invoice (id),
    article_id integer NOT NULL REFERENCES article (id),
    quantity   integer NOT NULL CHECK (quantity > 0),
    unit_price numeric(10,2) NOT NULL,
    line_net   numeric(10,2) NOT NULL
);

CREATE TABLE stock_movement (
    id         serial PRIMARY KEY,
    article_id integer NOT NULL REFERENCES article (id),
    quantity   integer NOT NULL,
    reason     text NOT NULL,
    invoice_id integer REFERENCES invoice (id)
);

CREATE TABLE counter (
    name  text PRIMARY KEY,
    value integer NOT NULL
);

CREATE INDEX invoice_customer_idx ON invoice (customer_id);
CREATE INDEX invoice_line_invoice_idx ON invoice_line (invoice_id);
CREATE INDEX stock_movement_article_idx ON stock_movement (article_id);
