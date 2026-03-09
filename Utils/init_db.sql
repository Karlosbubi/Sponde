-- For Postgressql
-- Version 1.0


-- Tables
    
create table if not exists category (
    id serial primary key,
    name varchar(255) not null
);

create table if not exists store (
    id serial primary key,
    name varchar(255) not null,
    description text
);

create table if not exists product (
    product_code varchar(255) primary key,
    name varchar(255) not null,
    description text
);

create table if not exists price (
    id serial primary key,
    product_code varchar(255) references product(product_code),
    price numeric(10,2) default 0,
    date date default current_date,
    isSpecialOffer boolean default false
);

create table if not exists cart (
    id serial primary key,
    store_id int references store(id),
    date date default current_date
);

create table if not exists purchase (
    id serial primary key,
    product_code varchar(255) references product(product_code),
    cart_id int references cart(id),
    quantity numeric(10,2) default 1
);

create table if not exists store_category (
    store_id int references store(id),
    category_id int references category(id),
    primary key (store_id, category_id)
);

create table if not exists product_category (
    product_code varchar(255) references product(product_code),
    category_id int references category(id),
    primary key (product_code, category_id)
)

-- Views