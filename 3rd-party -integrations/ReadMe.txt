Here is the place to put any 3rd party integrations to share with everyone.
 
Third-Party Integration Guide

# Overview

Our application supports third-party integration via Named Pipes, allowing external programs to communicate with it as independent processes.

This integration model is similar to a plugin system, but plugins run as separate applications rather than being loaded into the main process.
As a result, integrations are language-agnostic, stable, and isolated.

Any program capable of opening a Named Pipe on the host system can integrate with our application.

# Why Named Pipes

We chose Named Pipes as the IPC (Inter-Process Communication) mechanism for the following reasons:

No programming language constraints
Clients can be written in C#, C++, Python, Rust, Go, Node.js, or any other language that supports Named Pipes.

Process isolation
Third-party tools run independently and cannot crash or block the main application.

Low latency and local communication
Named Pipes provide fast and secure communication on the local machine.

Clear versioning and protocol control
The integration protocol is explicitly defined and versioned.

# Architecture
+---------------------+       Named Pipe       +-------------------------------+
| Third-Party Program | <-------------------> | EezBotFun Config Application   |
| (Any Language)      |                       | (Pipe Server)                  |
+---------------------+                       +---------------------------------+


The main application acts as the Named Pipe server

Third-party integrations act as Named Pipe clients

Communication is bidirectional


See EZBF_IPC_PROTOCOL.txt for details.
