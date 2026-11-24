-- SPDX-License-Identifier: AGPL-3.0-or-later
-- Copyright (c) 2026 Robert Binsack
--
-- Table of the example entity in app/entities.js.
CREATE TABLE IF NOT EXISTS app_item (
    id       serial PRIMARY KEY,
    title    text NOT NULL,
    quantity integer NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    price    numeric(10,2) CHECK (price >= 0),
    due      date,
    done     boolean
);
