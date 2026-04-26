namespace VeloxDev.Docs.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        public CodeProvider CSharpDemo { get; } = new CodeProvider
        {
            Language = "csharp",
            Code = """
                using System;
                using System.Collections.Generic;
                using System.Linq;

                namespace HelloWorld;

                public class Program
                {
                    public static void Main(string[] args)
                    {
                        // Say hello
                        Console.WriteLine("Hello, VeloxDev!");
                        var numbers = Enumerable.Range(1, 5);
                        foreach (var n in numbers)
                        {
                            Console.WriteLine($"  {n} * {n} = {n * n}");
                        }
                    }
                }
                """
        };

        public CodeProvider PythonDemo { get; } = new CodeProvider
        {
            Language = "python",
            Code = """
                from dataclasses import dataclass
                from typing import List

                @dataclass
                class Point:
                    x: float
                    y: float

                    def distance(self, other: "Point") -> float:
                        return ((self.x - other.x) ** 2 + (self.y - other.y) ** 2) ** 0.5

                def main() -> None:
                    points: List[Point] = [Point(0, 0), Point(3, 4)]
                    dist = points[0].distance(points[1])
                    print(f"Distance: {dist:.2f}")   # 5.00

                if __name__ == "__main__":
                    main()
                """
        };

        public CodeProvider RustDemo { get; } = new CodeProvider
        {
            Language = "rust",
            Code = """
                use std::fmt;

                #[derive(Debug)]
                struct Matrix {
                    data: Vec<Vec<f64>>,
                    rows: usize,
                    cols: usize,
                }

                impl Matrix {
                    fn new(rows: usize, cols: usize) -> Self {
                        Self { data: vec![vec![0.0; cols]; rows], rows, cols }
                    }

                    fn set(&mut self, r: usize, c: usize, val: f64) {
                        self.data[r][c] = val;
                    }
                }

                impl fmt::Display for Matrix {
                    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
                        for row in &self.data {
                            let s: Vec<String> = row.iter().map(|x| format!("{x:6.2}")).collect();
                            writeln!(f, "[ {} ]", s.join(", "))?;
                        }
                        Ok(())
                    }
                }

                fn main() {
                    let mut m = Matrix::new(3, 3);
                    m.set(0, 0, 1.0);
                    m.set(1, 1, 2.0);
                    m.set(2, 2, 3.0);
                    println!("{m}");
                }
                """
        };

        public CodeProvider TypeScriptDemo { get; } = new CodeProvider
        {
            Language = "typescript",
            Code = """
                interface User {
                    id: number;
                    name: string;
                    email?: string;
                }

                type ApiResponse<T> = {
                    data: T;
                    status: number;
                    message: string;
                };

                async function fetchUser(id: number): Promise<ApiResponse<User>> {
                    const res = await fetch(`/api/users/${id}`);
                    if (!res.ok) throw new Error(`HTTP ${res.status}`);
                    const data: User = await res.json();
                    return { data, status: res.status, message: "ok" };
                }

                // Entry point
                (async () => {
                    const response = await fetchUser(1);
                    console.log(`Hello, ${response.data.name}!`);
                })();
                """
        };

        public CodeProvider SqlDemo { get; } = new CodeProvider
        {
            Language = "sql",
            Code = """
                -- Top selling products per category
                WITH ranked AS (
                    SELECT
                        p.category_id,
                        p.product_id,
                        p.name,
                        SUM(oi.quantity * oi.unit_price) AS revenue,
                        RANK() OVER (
                            PARTITION BY p.category_id
                            ORDER BY SUM(oi.quantity * oi.unit_price) DESC
                        ) AS rnk
                    FROM order_items oi
                    JOIN products p ON p.product_id = oi.product_id
                    WHERE oi.created_at >= '2024-01-01'
                    GROUP BY p.category_id, p.product_id, p.name
                )
                SELECT category_id, product_id, name, revenue
                FROM ranked
                WHERE rnk <= 3
                ORDER BY category_id, rnk;
                """
        };

        public CodeProvider YamlDemo { get; } = new CodeProvider
        {
            Language = "yaml",
            Code = """
                name: veloxdev-docs
                version: "1.0.0"

                services:
                  app:
                    image: veloxdev/docs:latest
                    ports:
                      - "8080:80"
                    environment:
                      ASPNETCORE_ENVIRONMENT: Production
                      ConnectionStrings__Default: "Host=db;Database=docs"
                    depends_on:
                      - db
                    restart: unless-stopped

                  db:
                    image: postgres:16
                    volumes:
                      - pgdata:/var/lib/postgresql/data
                    environment:
                      POSTGRES_DB: docs
                      POSTGRES_USER: admin
                      POSTGRES_PASSWORD: secret

                volumes:
                  pgdata:
                """
        };
    }
}
