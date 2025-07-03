# Geotab SDK Challenge

A minimal .NET console application to perform incremental backups of vehicle data from the Geotab API.

---

## Challenge Description

Build a minimal containerized application that incrementally backs up vehicle data using the Geotab API.

**Requirements:**

- Retrieve all available vehicles from the Geotab platform and create one CSV file per vehicle.
- Each CSV must include: Vehicle ID, Timestamp, VIN, Latitude, Longitude, and Odometer.
- Support incremental updates: append only new data (e.g., every minute) without duplicating entries.
- Implement the app in any language, preferably C# (.NET), Java, TypeScript/JavaScript, or Python. Prioritize clarity and minimal dependencies.
- Package the solution as an OCI-compliant container image. It must build and run cleanly without requiring local setup beyond the container runtime.
- Validate that the downloaded data matches what’s visible in the MyGeotab web interface (use the UI to verify).

**Evaluation Criteria:**

- Simplicity: Clear, minimal, and easy to understand — a single-file solution is ideal.
- Correctness: Accurately retrieves and appends vehicle data.
- Portability: Fully functional within an OCI-compliant container.
- Clarity: Well-structured code with clear instructions for building and running.

© Geotab Inc. All Rights Reserved

---

## Prerequisites

- Docker installed and running on your system.
- Valid Geotab API credentials (server, database, username, and password).

---

## Build the Docker image

From the root folder of the project (where the Dockerfile is), run:

```bash
docker build -t geotab-sdk-challenge .
```

---

## Build the Docker image

From the root folder of the project (where the Dockerfile is), run:

```bash
docker run --rm geotab-sdk-challenge <server> <database> <username> <password>
```

---

## Persist backup files locally

Backup CSV files are saved inside the container under /app/VehicleBackups.
To keep files locally on your host machine, mount a volume:

Linux/macOS:

```bash
docker run --rm -v $(pwd)/VehicleData:/app/VehicleBackups geotab-sdk-challenge <server> <database> <username> <password>
```

Windows (PowerShell):

```bash
docker run --rm -v ${PWD}/VehicleData:/app/VehicleBackups geotab-sdk-challenge <server> <database> <username> <password>
```

This way, backups are synced to your local VehicleData folder.

---

## Behavior

- The app runs backup cycles every minute, continuously until cancelled (Ctrl+C).
- Each cycle fetches updated data and appends new records to per-vehicle CSV files.

---

## Cross-platform support

Using Docker ensures the app runs on any OS with Docker installed, without local dependencies.

---

## Contact

For questions or feedback, contact me via email at miguelnerokcontact@gmail.com.