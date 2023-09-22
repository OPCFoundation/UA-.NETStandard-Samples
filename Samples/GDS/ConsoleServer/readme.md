## GDS Docker Deployment

### Docker Compose
To run the GDS using docker compose, simply clone the repository and execute ```docker compose  up -d``` in this directory.
If you can not use ```network_mode: host``` follow the instructions written in the ```docker-compose.yml``` file to configure the ports and hostname directly. 

### Docker
To run the GDS using docker first build the container. To do this either run the script ```dockerbuild.bat``` or manually build the container by running ```docker build -t gds .```. 
To execute the gds container run one of the following commands in this directory:
#### No exposed volumes (no direct certificate or configuration management or logs) - w/ network mode host
```docker run -d --network host --restart always --name dockergds gds:latest```
#### No exposed volumes (no direct certificate or configuration management or logs)
```docker run -d -p 58810:58812 -h <hostname> --restart always --name dockergds gds:latest```

#### With exposed volumes (no direct certificate or configuration management or logs) - w/ network mode host
```docker run -d -v ./GDS-data/data:/root/.local/share/ -v ./GDS-data/config/:/gds/config --network host --restart always --name dockergds gds:latest```
#### With exposed volumes (no direct certificate or configuration management or logs)
```docker run -d -v ./GDS-data/data:/root/.local/share/ -v ./GDS-data/config/:/gds/config -p 58810:58812 -h <hostname> --restart always --name dockergds gds:latest```