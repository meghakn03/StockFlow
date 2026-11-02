# REST API

Authenticated OpenAPI JSON: `/openapi/v1.json`. Sign in at `/login`, or POST JSON to `/api/auth/login`. GET `/api/auth/csrf` with the session cookie, then send its `token` as `X-CSRF-TOKEN` on every mutation. GET `/api/auth/me` returns the current email and role. POST `/api/auth/logout` ends the session.

| Method | Route | Purpose | Role |
|---|---|---|---|
| GET | /api/dashboard | Whole-database workspace totals | Both |
| GET | /api/products?search=&lowStock=true&page=1&pageSize=20 | Catalogue page | Both |
| GET | /api/products/{id} | Product | Both |
| POST | /api/products | Create (zero opening stock) | Admin |
| PUT | /api/products/{id} | Update catalogue fields | Admin |
| DELETE | /api/products/{id} | Archive | Admin |
| POST | /api/products/{id}/receive | Receive positive quantity, with reference | Both |
| GET | /api/customers?search=&page=1&pageSize=20 | Customers page | Both |
| GET | /api/customers/{id} | Customer details | Both |
| POST / PUT | /api/customers[/{id}] | Create / update | Both |
| GET | /api/orders?status=&search=&page=1&pageSize=20 | Orders page (search customer name) | Both |
| GET | /api/orders/{id} | Lines, customer, captured prices | Both |
| POST | /api/orders | Create immutable draft | Both |
| POST | /api/orders/{id}/confirm | Atomically reserve all lines | Both |
| POST | /api/orders/{id}/ship | Consume physical and reserved stock | Both |
| POST | /api/orders/{id}/cancel | Cancel and release stock | Both |
| GET | /api/reports/sales | Monthly shipped revenue and cumulative revenue | Both |
| GET | /api/reports/top-products | Revenue-ranked products | Both |
| GET | /api/reports/low-stock | Available stock at/below reorder threshold | Both |
| GET | /api/audit?page=1 | Latest events, 50 per page | Admin |
| GET | /health | Database readiness | Public |

Product body: `{"sku":"SF-100","name":"Canvas tote","category":"Accessories","price":18.00,"reorderLevel":5}`

Customer body: `{"name":"Alex Smith","email":"alex@example.com","address":"10 High Street"}`

Receipt body: `{"quantity":10,"reference":"PO-1042"}`

Order body: `{"customerId":1,"lines":[{"productId":1,"quantity":2}]}`

Lists return `{items,total,page,pageSize}`. Page size defaults to 20, maximum 100. State transitions return 204. Creation returns 201 and a Location header. Validation returns 400, insufficient stock/invalid transitions/unique conflicts return 409, missing records return 404, unauthenticated requests 401, insufficient role 403. Error bodies use Problem Details. No stock values are accepted in product CRUD input.
